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
    private int _updateCount;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private HardwareInfo _hardware = new();

    public ObservableCollection<DriverItem> Devices { get; } = new();
    public ObservableCollection<DriverItem> PendingUpdates { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

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
        StatusText = "Scanning hardware devices...";
        HasSummary = false;
        _driverCts = new CancellationTokenSource();

        Devices.Clear();
        var scanned = await _driverService.ScanDevicesAsync(AddLog, _driverCts.Token);
        foreach (var item in scanned)
        {
            Devices.Add(item);
        }

        HasScanned = true;
        IsScanning = false;
        StatusText = $"Hardware scan complete. {Devices.Count} devices analyzed.";
    }

    [RelayCommand]
    private async Task CheckOnlineUpdates()
    {
        IsScanning = true;
        StatusText = "Searching Windows Update Catalog for drivers...";
        HasSummary = false;
        _driverCts = new CancellationTokenSource();

        PendingUpdates.Clear();
        var updates = await _driverService.CheckOnlineDriverUpdatesAsync(AddLog, _driverCts.Token);
        foreach (var item in updates)
        {
            PendingUpdates.Add(item);
        }

        UpdateCount = PendingUpdates.Count;
        IsScanning = false;

        if (UpdateCount > 0)
        {
            StatusText = $"Found {UpdateCount} certified driver update(s) ready to install.";
            SummaryMessage = $"{UpdateCount} driver updates available.";
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
    private async Task InstallDriverUpdates()
    {
        if (PendingUpdates.Count == 0)
        {
            StatusText = "No driver updates selected.";
            return;
        }

        IsInstalling = true;
        StatusText = "Downloading and installing certified drivers...";
        _driverCts = new CancellationTokenSource();

        var success = await _driverService.InstallDriverUpdatesAsync(AddLog, cancellationToken: _driverCts.Token);

        IsInstalling = false;
        if (success)
        {
            StatusText = $"Driver installation completed!";
            SummaryMessage = $"Successfully processed driver updates. A reboot may be recommended.";
            HasSummary = true;
            PendingUpdates.Clear();
            UpdateCount = 0;
        }
        else
        {
            StatusText = "Driver update process encountered issues. Check log.";
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
    }

    private void AddLog(string msg)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
