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

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

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
        StatusText = UpdateCount > 0 
            ? $"Found {UpdateCount} certified driver update(s) online." 
            : "All drivers are up to date in the Windows Update Catalog.";
    }

    [RelayCommand]
    private async Task InstallDriverUpdates()
    {
        if (PendingUpdates.Count == 0) return;

        IsInstalling = true;
        HasSummary = false;
        SummaryMessage = string.Empty;
        StatusText = "Downloading and installing driver updates sequentially...";
        _driverCts = new CancellationTokenSource();

        // Mark all as queued
        foreach (var driver in PendingUpdates)
        {
            driver.Status = "Queued";
            driver.StatusMessage = "Waiting in queue...";
            driver.IsBusy = false;
            driver.IsSuccess = false;
            driver.IsFailed = false;
            driver.Progress = 0;
        }

        var success = await _driverService.InstallDriverUpdatesAsync(
            onLog: AddLog,
            onDriverEvent: (idx, stage, detail) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (idx >= 0 && idx < PendingUpdates.Count)
                    {
                        var driver = PendingUpdates[idx];
                        switch (stage)
                        {
                            case "DOWNLOADING":
                                driver.Status = "Downloading...";
                                driver.StatusMessage = "Downloading package from Microsoft Catalog...";
                                driver.IsBusy = true;
                                driver.Progress = 35;
                                break;

                            case "INSTALLING":
                                driver.Status = "Installing...";
                                driver.StatusMessage = "Installing driver silently into Windows...";
                                driver.Progress = 70;
                                break;

                            case "SUCCESS":
                                driver.Status = "Installed ✓";
                                driver.StatusMessage = "Installed successfully!";
                                driver.IsBusy = false;
                                driver.IsSuccess = true;
                                driver.Progress = 100;
                                break;

                            case "FAILED":
                                driver.Status = "Failed ✗";
                                driver.StatusMessage = string.IsNullOrWhiteSpace(detail) ? "Installation failed" : detail;
                                driver.ErrorMessage = detail;
                                driver.IsBusy = false;
                                driver.IsFailed = true;
                                break;
                        }
                    }
                });
            },
            cancellationToken: _driverCts.Token);

        IsInstalling = false;

        var succeeded = PendingUpdates.Count(d => d.IsSuccess);
        var failed = PendingUpdates.Count(d => d.IsFailed);

        SummaryMessage = failed == 0 && succeeded > 0
            ? $"🎉 Success! All {succeeded} driver updates were installed successfully."
            : $"Installation finished: {succeeded} succeeded, {failed} failed. See details below.";

        HasSummary = true;
        StatusText = SummaryMessage;
        AddLog($"[Status] {SummaryMessage}");
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
