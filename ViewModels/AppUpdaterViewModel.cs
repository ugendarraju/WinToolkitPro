using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class AppUpdaterViewModel : ObservableObject
{
    private readonly WingetService _wingetService;
    private CancellationTokenSource? _updateCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isUpgrading;

    [ObservableProperty]
    private string _statusText = "Click 'Scan for Updates' to find updates for installed applications.";

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private int _upgradeCount;

    [ObservableProperty]
    private int _selectedUpgradeCount;

    public ObservableCollection<AppUpgradeItem> AllUpgrades { get; } = new();
    public ObservableCollection<AppUpgradeItem> FilteredUpgrades { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public AppUpdaterViewModel(WingetService wingetService)
    {
        _wingetService = wingetService;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredUpgrades.Clear();
        var query = SearchQuery?.Trim() ?? string.Empty;

        var matches = string.IsNullOrWhiteSpace(query)
            ? AllUpgrades
            : AllUpgrades.Where(u =>
                u.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.PackageId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.InstalledVersion.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.AvailableVersion.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var item in matches)
        {
            FilteredUpgrades.Add(item);
        }
    }

    [RelayCommand]
    public async Task ScanUpgrades()
    {
        if (IsScanning || IsUpgrading) return;

        IsScanning = true;
        StatusText = "Scanning installed applications for updates via Windows Package Manager...";
        AllUpgrades.Clear();
        FilteredUpgrades.Clear();
        _updateCts = new CancellationTokenSource();

        AddLog("Scanning installed software for available updates...");

        try
        {
            var upgrades = await _wingetService.ScanUpgradesAsync(AddLog, _updateCts.Token);
            foreach (var u in upgrades)
            {
                u.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(AppUpgradeItem.IsSelected))
                    {
                        UpdateSelectionCount();
                    }
                };
                AllUpgrades.Add(u);
            }

            ApplyFilter();
            UpgradeCount = AllUpgrades.Count;
            UpdateSelectionCount();

            StatusText = UpgradeCount > 0
                ? $"Found {UpgradeCount} installed application(s) with newer versions available."
                : "All installed applications are currently up to date!";
            AddLog(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            AddLog(StatusText);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var u in FilteredUpgrades)
        {
            u.IsSelected = true;
        }
        UpdateSelectionCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var u in FilteredUpgrades)
        {
            u.IsSelected = false;
        }
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        SelectedUpgradeCount = AllUpgrades.Count(u => u.IsSelected);
    }

    [RelayCommand]
    private async Task UpgradeSelected()
    {
        var targets = AllUpgrades.Where(u => u.IsSelected).ToList();
        if (targets.Count == 0)
        {
            StatusText = "Please select at least one application to update.";
            return;
        }

        IsUpgrading = true;
        _updateCts = new CancellationTokenSource();
        var ct = _updateCts.Token;

        var total = targets.Count;
        var current = 0;

        AddLog($"Starting batch upgrade of {total} application(s)...");

        try
        {
            foreach (var item in targets)
            {
                if (ct.IsCancellationRequested) break;

                current++;
                StatusText = $"Updating {item.Name} ({current}/{total})...";
                OverallProgress = ((double)(current - 1) / total) * 100.0;

                await _wingetService.UpgradeAppAsync(item, AddLog, ct);

                OverallProgress = ((double)current / total) * 100.0;
            }

            StatusText = ct.IsCancellationRequested ? "App upgrade cancelled." : "All selected applications updated successfully!";
            AddLog(StatusText);
        }
        finally
        {
            await _wingetService.CleanTempFilesAsync(AddLog);
            IsUpgrading = false;
        }
    }

    [RelayCommand]
    private void CancelUpgrade()
    {
        _updateCts?.Cancel();
        StatusText = "Cancelling updates...";
        AddLog("User requested cancellation of application updates.");
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
