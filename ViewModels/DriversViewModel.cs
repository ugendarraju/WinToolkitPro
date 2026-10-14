using System.Collections.ObjectModel;
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
    private int _updateCount;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private string _selectedDriverSource = "⚡ Snappy / SamDrivers (Newest)";

    public ObservableCollection<string> DriverSources { get; } = new()
    {
        "⚡ Snappy / SamDrivers (Newest)",
        "🌐 Microsoft Update Catalog (WHQL)",
        "🏢 Direct Vendor Repositories"
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
        AddLog("Generating detailed Windows Battery Life Report...");
        var path = await _hardwareService.GenerateAndOpenBatteryReportAsync();
        if (!string.IsNullOrEmpty(path))
        {
            AddLog($"✅ Battery report generated and opened: {path}");
        }
        else
        {
            AddLog("⚠️ Could not generate battery report (device may not have a battery).");
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
    public async Task LaunchSnappyGui()
    {
        AddLog("Launching Snappy Driver Installer Origin GUI...");
        await _driverService.LaunchSnappyGuiAsync(AddLog);
    }

    [RelayCommand]
    private async Task CheckOnlineUpdates()
    {
        IsScanning = true;
        StatusText = $"Searching {SelectedDriverSource} for drivers...";
        HasSummary = false;
        _driverCts = new CancellationTokenSource();

        PendingUpdates.Clear();
        var updates = await _driverService.CheckDriverUpdatesBySourceAsync(SelectedDriverSource, AddLog, _driverCts.Token);
        foreach (var item in updates)
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
                Devices.Add(item);
            }
            UpdateSelectedDriverCount();
            StatusText = $"Found {UpdateCount} driver(s) via {SelectedDriverSource}.";
            SummaryMessage = $"{UpdateCount} driver update(s) ready. Select drivers and click Install.";
            HasSummary = true;
        }
        else
        {
            StatusText = "All drivers are up to date.";
            SummaryMessage = "No driver updates required. Your system hardware is fully up to date.";
            HasSummary = true;
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
        bool success;

        if (SelectedDriverSource.Contains("Snappy", StringComparison.OrdinalIgnoreCase))
        {
            success = await _driverService.InstallSnappyDriverUpdatesAsync(selectedIds, AddLog, cancellationToken: _driverCts.Token);
        }
        else
        {
            success = await _driverService.InstallDriverUpdatesAsync(selectedIds, AddLog, cancellationToken: _driverCts.Token);
        }

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
