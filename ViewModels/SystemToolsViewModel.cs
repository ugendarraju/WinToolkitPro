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
    private bool _showBloatwareOnly = true;

    [ObservableProperty]
    private int _selectedAppCount;

    [ObservableProperty]
    private int _totalAppCount;

    [ObservableProperty]
    private int _bloatwareCount;

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
    partial void OnShowBloatwareOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        var q = SearchQuery?.Trim() ?? string.Empty;

        var baseList = ShowBloatwareOnly 
            ? AllApps.Where(a => a.IsBloatware) 
            : AllApps;

        var matches = string.IsNullOrWhiteSpace(q)
            ? baseList
            : baseList.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches) FilteredApps.Add(item);
    }

    [RelayCommand]
    public async Task ScanApps()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Scanning for preinstalled Windows bloatware packages...";
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
            BloatwareCount = AllApps.Count(a => a.IsBloatware);
            UpdateSelectedCount();

            StatusMessage = BloatwareCount > 0
                ? $"Scan completed: {BloatwareCount} bloatware packages detected ({SelectedAppCount} selected for debloat)."
                : "Scan completed: No Windows bloatware packages detected on this system.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var a in FilteredApps)
        {
            if (!a.IsSystemCritical)
                a.IsSelected = true;
        }
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
            a.IsSelected = a.IsBloatware && !a.IsSystemCritical;
        }
        UpdateSelectedCount();
        AddLog($"Selected {AllApps.Count(a => a.IsSelected)} safe-to-remove bloatware package(s).");
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
            StatusMessage = "Please select at least one bloatware package to uninstall.";
            return;
        }

        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        var ct = _activeCts.Token;

        AddLog($"Starting batch removal of {targets.Count} package(s)...");
        int successCount = 0;

        try
        {
            foreach (var item in targets)
            {
                if (ct.IsCancellationRequested)
                {
                    AddLog("Debloat operation was cancelled by user.");
                    break;
                }

                StatusMessage = $"Uninstalling {item.DisplayName}...";
                var ok = await _systemToolsService.UninstallUwpAppAsync(item, AddLog);
                if (ok) successCount++;
            }

            StatusMessage = $"Debloat complete. Successfully removed {successCount} of {targets.Count} packages.";
            AddLog($"[Debloat] Finished: {successCount}/{targets.Count} removed.");
            
            // Remove uninstalled items from AllApps and re-filter
            var removed = AllApps.Where(a => a.IsRemoved).ToList();
            foreach (var r in removed) AllApps.Remove(r);
            ApplyFilter();
            UpdateSelectedCount();
            BloatwareCount = AllApps.Count(a => a.IsBloatware);
        }
        finally
        {
            IsBusy = false;
            _activeCts = null;
        }
    }

    [RelayCommand]
    private async Task RunSfc()
    {
        if (IsBusy) return;
        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        StatusMessage = "Running System File Checker (sfc /scannow)...";

        try
        {
            await _systemToolsService.RunSfcScanAsync(AddLog, _activeCts.Token);
            StatusMessage = "SFC scan finished. Check log for details.";
        }
        finally
        {
            IsBusy = false;
            _activeCts = null;
        }
    }

    [RelayCommand]
    private async Task RunDismRestoreHealth()
    {
        if (IsBusy) return;
        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        StatusMessage = "Running DISM Component Store RestoreHealth...";

        try
        {
            await _systemToolsService.RunDismRestoreHealthAsync(AddLog, _activeCts.Token);
            StatusMessage = "DISM RestoreHealth completed.";
        }
        finally
        {
            IsBusy = false;
            _activeCts = null;
        }
    }

    [RelayCommand]
    private async Task RunDismCleanup()
    {
        if (IsBusy) return;
        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        StatusMessage = "Running WinSxS Component Store Deep Cleanup...";

        try
        {
            await _systemToolsService.RunDismComponentCleanupAsync(AddLog, _activeCts.Token);
            StatusMessage = "WinSxS Component Store deep cleanup completed.";
        }
        finally
        {
            IsBusy = false;
            _activeCts = null;
        }
    }

    [RelayCommand]
    private async Task FlushNetwork()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Resetting network stack and flushing DNS...";

        try
        {
            await _systemToolsService.FlushDnsAndResetNetworkAsync(AddLog);
            StatusMessage = "Network cache flushed and sockets reset.";
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
