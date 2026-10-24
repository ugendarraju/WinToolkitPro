using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class DriversViewModel : ObservableObject
{
    private readonly DriverService _driverService;
    private readonly HardwareService _hardwareService;
    private CancellationTokenSource? _driverCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private bool _isSpecsLoading;

    [ObservableProperty]
    private string _statusText = "Ready to scan for hardware drivers";

    [ObservableProperty]
    private int _selectedDriverCount;

    [ObservableProperty]
    private string _installButtonText = "⚡ Install Selected Drivers";

    [ObservableProperty]
    private int _updateCount;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string _selectedDriverSource = "🌐 Microsoft Update Catalog (WHQL - Direct HTTPS)";

    public ObservableCollection<string> DriverSources { get; } = new()
    {
        "🌐 Microsoft Update Catalog (WHQL - Direct HTTPS)",
        "🏢 Direct Vendor Repositories (Intel / AMD / NVIDIA)"
    };

    [ObservableProperty]
    private HardwareInfo _hardware = new();

    public ObservableCollection<DriverItem> Devices { get; } = new();
    public ObservableCollection<DriverItem> PendingUpdates { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public string DriverArchNotice => OsEnvironmentService.Instance.Is64BitOperatingSystem 
        ? "Target: x64 Drivers" 
        : "Target: x86 Drivers";

    public DriversViewModel(DriverService driverService, HardwareService hardwareService)
    {
        _driverService = driverService;
        _hardwareService = hardwareService;
    }

    public async Task InitializeAsync()
    {
        await LoadSystemSpecs();
    }

    [RelayCommand]
    public async Task LoadSystemSpecs()
    {
        if (IsSpecsLoading) return;
        IsSpecsLoading = true;

        try
        {
            Hardware = await _hardwareService.GetHardwareInfoAsync();
            AddLog($"System specs loaded: {Hardware.CpuName}, {Hardware.RamTotal} RAM, {Hardware.GpuName}");
        }
        catch (Exception ex)
        {
            AddLog($"Error reading hardware specs: {ex.Message}");
        }
        finally
        {
            IsSpecsLoading = false;
        }
    }

    [RelayCommand]
    public async Task GenerateBatteryReport()
    {
        try
        {
            StatusText = "Generating Windows battery health report...";
            AddLog("Generating detailed Windows Battery Life Report via powercfg...");
            var path = await _hardwareService.GenerateAndOpenBatteryReportAsync();
            if (!string.IsNullOrEmpty(path))
            {
                StatusText = "Battery report generated and opened in browser.";
                AddLog($"✅ Battery report generated and opened: {path}");

                try
                {
                    if (Hardware != null && File.Exists(path))
                    {
                        var html = await File.ReadAllTextAsync(path);
                        HardwareService.ParseBatteryHtml(html, Hardware);
                    }
                }
                catch { }
            }
            else
            {
                StatusText = "Could not generate battery report.";
                AddLog("⚠️ Could not generate battery report (no battery detected or access restricted).");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Battery report error: {ex.Message}";
            AddLog($"⚠️ Error generating battery report: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ScanHardware()
    {
        IsScanning = true;
        StatusText = "Scanning hardware devices and driver states...";
        HasSummary = false;
        _driverCts = new CancellationTokenSource();

        Devices.Clear();
        var scanned = await _driverService.ScanDevicesAsync(AddLog, _driverCts.Token);
        foreach (var item in scanned)
        {
            item.IsSelected = item.HasUpdate;
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DriverItem.IsSelected)) UpdateSelectedDriverCount();
            };
            Devices.Add(item);
        }

        UpdateSelectedDriverCount();
        HasScanned = true;
        IsScanning = false;
        StatusText = $"Hardware scan complete. {Devices.Count} devices analyzed.";
    }

    [RelayCommand]
    public void OpenDeviceManager()
    {
        AddLog("Opening Windows Device Manager (devmgmt.msc)...");
        _driverService.OpenDeviceManager();
    }

    [RelayCommand]
    private async Task CheckOnlineUpdates()
    {
        IsScanning = true;
        StatusText = $"Searching {SelectedDriverSource} for drivers...";
        HasSummary = false;
        _driverCts = new CancellationTokenSource();

        PendingUpdates.Clear();
        var results = await _driverService.CheckDriverUpdatesBySourceAsync(SelectedDriverSource, AddLog, _driverCts.Token);
        var actualUpdates = results.Where(r => r.HasUpdate).ToList();

        foreach (var item in actualUpdates)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DriverItem.IsSelected)) UpdateSelectedDriverCount();
            };
            PendingUpdates.Add(item);
        }

        UpdateCount = PendingUpdates.Count;
        IsScanning = false;

        if (UpdateCount > 0)
        {
            // Switch display to show available updates with selection checkboxes
            Devices.Clear();
            foreach (var item in PendingUpdates)
            {
                item.IsSelected = true;
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DriverItem.IsSelected)) UpdateSelectedDriverCount();
                };
                Devices.Add(item);
            }
            UpdateSelectedDriverCount();
            StatusText = $"Found {UpdateCount} driver update(s) via {SelectedDriverSource}.";
            SummaryMessage = $"{UpdateCount} driver update(s) ready to install. Select drivers and click Install.";
            HasSummary = true;
        }
        else
        {
            // All drivers are healthy and up to date! Show all devices with checkboxes cleared.
            Devices.Clear();
            foreach (var item in results)
            {
                item.IsSelected = false;
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DriverItem.IsSelected)) UpdateSelectedDriverCount();
                };
                Devices.Add(item);
            }
            UpdateSelectedDriverCount();
            StatusText = "All hardware drivers are up to date.";
            SummaryMessage = $"No updates required. All {results.Count} hardware devices have the latest certified drivers installed.";
            HasSummary = true;
        }
    }

    [RelayCommand]
    private void SelectUpdatesOnly()
    {
        foreach (var item in Devices)
        {
            item.IsSelected = item.HasUpdate;
        }
        UpdateSelectedDriverCount();
        if (SelectedDriverCount > 0)
        {
            StatusText = $"Selected {SelectedDriverCount} driver(s) with available updates.";
        }
        else
        {
            StatusText = "No driver updates to select. All devices are up to date.";
        }
    }

    [RelayCommand]
    private void SelectAllDrivers()
    {
        foreach (var item in Devices)
        {
            item.IsSelected = true;
        }
        UpdateSelectedDriverCount();
    }

    [RelayCommand]
    private void DeselectAllDrivers()
    {
        foreach (var item in Devices)
        {
            item.IsSelected = false;
        }
        UpdateSelectedDriverCount();
    }

    private void UpdateSelectedDriverCount()
    {
        SelectedDriverCount = Devices.Count(d => d.IsSelected);
        InstallButtonText = SelectedDriverCount > 0 
            ? $"⚡ Install Selected Drivers ({SelectedDriverCount})" 
            : "⚡ Install Selected Drivers";
    }

    [RelayCommand]
    private async Task InstallDriverUpdates()
    {
        var selected = Devices.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Please select at least one driver to install.";
            SummaryMessage = "No drivers selected. Check the boxes next to the required drivers.";
            HasSummary = true;
            return;
        }

        IsInstalling = true;
        StatusText = $"Installing {selected.Count} driver(s) via {SelectedDriverSource}...";
        _driverCts = new CancellationTokenSource();

        var taskItems = selected.Select(d => new ActivityTaskItem
        {
            Id = !string.IsNullOrEmpty(d.HardwareId) ? d.HardwareId : d.DeviceName,
            Name = d.DeviceName,
            Category = d.Manufacturer ?? "Hardware Driver",
            StatusMessage = "Queued..."
        }).ToList();

        ActivityService.Instance.StartSession("Hardware & Driver Updates", taskItems);

        foreach (var task in taskItems)
        {
            ActivityService.Instance.UpdateTask(task.Id, ActivityTaskStatus.Running, "Installing driver...", 50);
        }

        var selectedIds = selected.Select(d => !string.IsNullOrEmpty(d.HardwareId) ? d.HardwareId : d.DeviceName);
        var success = await _driverService.InstallDriverUpdatesAsync(selectedIds, AddLog, cancellationToken: _driverCts.Token);

        IsInstalling = false;
        foreach (var task in taskItems)
        {
            ActivityService.Instance.UpdateTask(task.Id, success ? ActivityTaskStatus.Completed : ActivityTaskStatus.Failed,
                success ? "Installed" : "Notice / Check logs", 100);
        }

        if (success)
        {
            StatusText = $"Driver installation completed!";
            SummaryMessage = $"Successfully processed {selected.Count} driver update(s) via {SelectedDriverSource}. A reboot may be recommended.";
            HasSummary = true;
            PendingUpdates.Clear();
            UpdateCount = 0;
            ActivityService.Instance.CompleteSession("Driver installation completed!");
        }
        else
        {
            StatusText = "Driver update process completed with notices. Check log.";
            ActivityService.Instance.CompleteSession("Driver update process completed with notices.");
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _driverCts?.Cancel();
        StatusText = "Operation cancelled.";
        AddLog("User cancelled driver operation.");
        IsScanning = false;
        IsInstalling = false;
        ActivityService.Instance.CompleteSession("Cancelled by user.");
    }

    private void AddLog(string msg)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
            ActivityService.Instance.AddLog(msg);
        });
    }
}
