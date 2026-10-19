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

        var taskItems = targets.Select(t => new ActivityTaskItem
        {
            Id = t.PackageId,
            Name = t.Name,
            Category = "Application Update",
            StatusMessage = $"Queued (v{t.AvailableVersion})..."
        }).ToList();

        ActivityService.Instance.StartSession("Software Updates", taskItems);
        AddLog($"Starting batch upgrade of {total} application(s)...");

        try
        {
            foreach (var item in targets)
            {
                if (ct.IsCancellationRequested) break;

                void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(AppUpgradeItem.Progress) || e.PropertyName == nameof(AppUpgradeItem.StatusMessage))
                    {
                        ActivityService.Instance.UpdateTask(item.PackageId, ActivityTaskStatus.Running, item.StatusMessage, item.Progress);
                    }
                }

                item.PropertyChanged += OnItemPropertyChanged;

                try
                {
                    current++;
                    StatusText = $"Updating {item.Name} ({current}/{total})...";
                    OverallProgress = ((double)(current - 1) / total) * 100.0;
                    ActivityService.Instance.UpdateOverallProgress(OverallProgress);
                    ActivityService.Instance.UpdateTask(item.PackageId, ActivityTaskStatus.Running, $"Updating to {item.AvailableVersion}...", item.Progress > 0 ? item.Progress : 15);

                    await _wingetService.UpgradeAppAsync(item, AddLog, ct);

                    var finalStatus = item.IsSuccess ? ActivityTaskStatus.Completed :
                                      item.IsFailed ? ActivityTaskStatus.Failed : ActivityTaskStatus.Completed;
                    ActivityService.Instance.UpdateTask(item.PackageId, finalStatus, item.StatusMessage, 100);

                    OverallProgress = ((double)current / total) * 100.0;
                    ActivityService.Instance.UpdateOverallProgress(OverallProgress);
                }
                finally
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                }
            }

            StatusText = ct.IsCancellationRequested ? "App upgrade cancelled." : "All selected applications updated successfully!";
            AddLog(StatusText);
            ActivityService.Instance.CompleteSession(StatusText);
        }
        finally
        {
            await _wingetService.CleanTempFilesAsync(AddLog);
            IsUpgrading = false;
        }
    }

    [RelayCommand]
    private async Task RetryUpgrade(AppUpgradeItem item)
    {
        if (IsUpgrading) return;
        _updateCts = new CancellationTokenSource();
        AddLog($"[Retry] Attempting update for {item.Name}...");

        var taskItem = new ActivityTaskItem
        {
            Id = item.PackageId,
            Name = item.Name,
            Category = "Application Update",
            StatusMessage = $"Updating to {item.AvailableVersion}...",
            Status = ActivityTaskStatus.Running
        };
        ActivityService.Instance.StartSession("Retry App Update", new List<ActivityTaskItem> { taskItem });

        await _wingetService.UpgradeAppAsync(item, AddLog, _updateCts.Token);

        var finalStatus = item.IsSuccess ? ActivityTaskStatus.Completed :
                          item.IsFailed ? ActivityTaskStatus.Failed : ActivityTaskStatus.Completed;
        ActivityService.Instance.UpdateTask(item.PackageId, finalStatus, item.StatusMessage, 100);
        ActivityService.Instance.CompleteSession(item.StatusMessage);
    }

    [RelayCommand]
    private void RetryInteractive(AppUpgradeItem item)
    {
        AddLog($"[Interactive] Launching elevated console installer for {item.Name}...");
        _wingetService.LaunchInteractiveUpgrade(item);
    }

    [RelayCommand]
    private void CancelUpgrade()
    {
        _updateCts?.Cancel();
        StatusText = "Cancelling updates...";
        AddLog("User requested cancellation of application updates.");
        ActivityService.Instance.CompleteSession("Cancelled by user.");
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
            ActivityService.Instance.AddLog(message);
        });
    }
}
