using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class DriversViewModel : ObservableObject
{
    private readonly DriverService _driverService;
    private CancellationTokenSource? _driverCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _statusText = "Ready to scan for hardware drivers";

    [ObservableProperty]
    private int _updateCount;

    public ObservableCollection<DriverItem> Devices { get; } = new();
    public ObservableCollection<DriverItem> PendingUpdates { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public DriversViewModel(DriverService driverService)
    {
        _driverService = driverService;
    }

    [RelayCommand]
    private async Task ScanHardware()
    {
        IsScanning = true;
        StatusText = "Scanning hardware devices...";
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
        _driverCts = new CancellationTokenSource();

        PendingUpdates.Clear();
        var updates = await _driverService.CheckOnlineDriverUpdatesAsync(AddLog, _driverCts.Token);
        foreach (var item in updates)
        {
            PendingUpdates.Add(item);
        }

        UpdateCount = PendingUpdates.Count;
        IsScanning = false;
        StatusText = UpdateCount > 0 
            ? $"Found {UpdateCount} certified driver update(s) online." 
            : "All drivers are up to date in the Windows Update Catalog.";
    }

    [RelayCommand]
    private async Task InstallDriverUpdates()
    {
        IsInstalling = true;
        StatusText = "Downloading and installing driver updates...";
        _driverCts = new CancellationTokenSource();

        var success = await _driverService.InstallDriverUpdatesAsync(AddLog, _driverCts.Token);

        IsInstalling = false;
        StatusText = success ? "Driver installation process completed!" : "Driver installation finished with notices.";
        AddLog(StatusText);

        // Re-check updates after installation
        await CheckOnlineUpdates();
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
