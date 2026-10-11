using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class SystemToolsViewModel : ObservableObject
{
    private readonly SystemToolsService _systemToolsService;
    private CancellationTokenSource? _activeCts;

    [ObservableProperty]
    private string _statusMessage = "Ready. Select a tool to begin.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _selectedAppCount;

    [ObservableProperty]
    private int _totalAppCount;

    [ObservableProperty]
    private string _currentDnsSelection = "Cloudflare";

    public ObservableCollection<UwpAppItem> AllApps { get; } = new();
    public ObservableCollection<UwpAppItem> FilteredApps { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public SystemToolsViewModel(SystemToolsService systemToolsService)
    {
        _systemToolsService = systemToolsService;
    }

    public async Task InitializeAsync()
    {
        await ScanApps();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;

        var matches = string.IsNullOrWhiteSpace(q)
            ? AllApps
            : AllApps.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches) FilteredApps.Add(item);
    }

    [RelayCommand]
    public async Task ScanApps()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Scanning installed Windows UWP packages...";
        AllApps.Clear();
        FilteredApps.Clear();

        try
        {
            var apps = await _systemToolsService.ScanUwpAppsAsync(AddLog);
            foreach (var a in apps)
            {
                a.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(UwpAppItem.IsSelected))
                    {
                        UpdateSelectedCount();
                    }
                };
                AllApps.Add(a);
            }

            ApplyFilter();
            TotalAppCount = AllApps.Count;
            UpdateSelectedCount();
            StatusMessage = $"Scan completed: {TotalAppCount} packages found ({AllApps.Count(a => a.IsBloatware)} detected as safe to remove).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var a in FilteredApps) a.IsSelected = true;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var a in FilteredApps) a.IsSelected = false;
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectSafeDebloat()
    {
        foreach (var a in AllApps)
        {
            a.IsSelected = a.IsBloatware;
        }
        UpdateSelectedCount();
        AddLog($"Selected {AllApps.Count(a => a.IsSelected)} safe-to-remove bloatware packages.");
    }

    private void UpdateSelectedCount()
    {
        SelectedAppCount = AllApps.Count(a => a.IsSelected && !a.IsRemoved);
    }

    [RelayCommand]
    private async Task UninstallSelected()
    {
        var targets = AllApps.Where(a => a.IsSelected && !a.IsRemoved).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Please select at least one UWP application to uninstall.";
            return;
        }

        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        var total = targets.Count;
        var current = 0;

        AddLog($"Starting batch debloat of {total} package(s)...");

        try
        {
            foreach (var app in targets)
            {
                if (_activeCts.IsCancellationRequested) break;
                current++;
                StatusMessage = $"Uninstalling {app.DisplayName} ({current}/{total})...";

                await _systemToolsService.UninstallUwpAppAsync(app, AddLog);
            }

            StatusMessage = _activeCts.IsCancellationRequested ? "Debloat cancelled." : "Batch debloat completed!";
            AddLog(StatusMessage);
        }
        finally
        {
            UpdateSelectedCount();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunSfc()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Running System File Checker (sfc /scannow)...";
        _activeCts = new CancellationTokenSource();

        try
        {
            await _systemToolsService.RunSfcScanAsync(AddLog, _activeCts.Token);
            StatusMessage = "SFC Scan completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunDismRestoreHealth()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Running DISM RestoreHealth...";
        _activeCts = new CancellationTokenSource();

        try
        {
            await _systemToolsService.RunDismRestoreHealthAsync(AddLog, _activeCts.Token);
            StatusMessage = "DISM Component Store Repair completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunDismCleanup()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Running WinSxS Component Store Deep Cleanup...";
        _activeCts = new CancellationTokenSource();

        try
        {
            await _systemToolsService.RunDismComponentCleanupAsync(AddLog, _activeCts.Token);
            StatusMessage = "WinSxS Component Store Cleanup completed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FlushNetwork()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Flushing DNS and resetting network stack...";

        try
        {
            await _systemToolsService.FlushDnsAndResetNetworkAsync(AddLog);
            StatusMessage = "Network sockets and DNS cache reset successfully.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetDns(string dnsType)
    {
        if (IsBusy) return;
        IsBusy = true;
        CurrentDnsSelection = dnsType;
        StatusMessage = $"Configuring DNS to {dnsType}...";

        try
        {
            await _systemToolsService.SetDnsServersAsync(dnsType, AddLog);
            StatusMessage = $"DNS set to {dnsType}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Creating System Restore Point...";

        try
        {
            await _systemToolsService.CreateRestorePointAsync("WinToolkit Pro Maintenance Checkpoint", AddLog);
            StatusMessage = "Restore Point creation routine finished.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _activeCts?.Cancel();
        StatusMessage = "Cancelling ongoing operation...";
        AddLog("User requested operation cancellation.");
    }

    private void AddLog(string msg)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Insert(0, line);
            if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
