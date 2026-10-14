using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class WindowsUpdateViewModel : ObservableObject
{
    private readonly WindowsUpdateService _updateService;
    private readonly PowerShellService _powerShellService;
    private CancellationTokenSource? _updateCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private string _statusText = "Ready to scan for Microsoft updates";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private double _totalSelectedSizeMB;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isCleanState;

    public ObservableCollection<WindowsUpdateItem> AvailableUpdates { get; } = new();
    public ObservableCollection<WindowsUpdateItem> FilteredUpdates { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public string TargetOsNotice => $"Target: {OsEnvironmentService.Instance.OsDisplayName}";

    public WindowsUpdateViewModel(WindowsUpdateService updateService, PowerShellService powerShellService)
    {
        _updateService = updateService;
        _powerShellService = powerShellService;
    }

    [RelayCommand]
    private async Task ScanUpdates()
    {
        IsScanning = true;
        StatusText = "Connecting to Microsoft Update Catalog & scanning...";
        HasSummary = false;
        SummaryMessage = string.Empty;
        _updateCts = new CancellationTokenSource();

        AvailableUpdates.Clear();
        FilteredUpdates.Clear();
        AddLog("Scanning for Windows OS and Microsoft software updates...");

        var updates = await _updateService.ScanUpdatesAsync(AddLog, _updateCts.Token);
        foreach (var item in updates)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(WindowsUpdateItem.IsSelected))
                {
                    UpdateSelectionSummary();
                }
            };
            AvailableUpdates.Add(item);
        }

        ApplyFilter();
        UpdateSelectionSummary();

        HasScanned = true;
        IsScanning = false;
        IsCleanState = AvailableUpdates.Count == 0;
        StatusText = AvailableUpdates.Count > 0
            ? $"Found {AvailableUpdates.Count} pending update(s)."
            : "Your system is up to date! No pending Microsoft updates found.";
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var u in AvailableUpdates)
        {
            u.IsSelected = true;
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var u in AvailableUpdates)
        {
            u.IsSelected = false;
        }
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private async Task InstallSelectedUpdates()
    {
        var selected = AvailableUpdates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Please select at least one update to install.";
            return;
        }

        IsInstalling = true;
        HasSummary = false;
        SummaryMessage = string.Empty;
        RebootRequired = false;
        StatusText = $"Downloading and installing {selected.Count} update(s)...";
        _updateCts = new CancellationTokenSource();

        // Mark selected as queued
        foreach (var u in selected)
        {
            u.StatusMessage = "Queued";
            u.IsBusy = true;
            u.IsSuccess = false;
            u.IsFailed = false;
            u.Progress = 0;
            u.ErrorMessage = string.Empty;
        }

        var taskItems = selected.Select(u => new ActivityTaskItem
        {
            Id = u.UpdateId,
            Name = !string.IsNullOrWhiteSpace(u.KBArticle) ? $"{u.KBArticle}: {u.Title}" : u.Title,
            Category = u.Category ?? "Windows Update",
            StatusMessage = "Queued..."
        }).ToList();

        ActivityService.Instance.StartSession("Windows Updates", taskItems);
        AddLog($"Starting installation of {selected.Count} update(s)...");

        var success = await _updateService.InstallSelectedUpdatesAsync(
            selected.Select(u => u.UpdateId),
            onLog: AddLog,
            onUpdateEvent: (updateId, stage, detail) =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    var item = AvailableUpdates.FirstOrDefault(u => u.UpdateId.Equals(updateId, StringComparison.OrdinalIgnoreCase));
                    if (item == null) return;

                    switch (stage)
                    {
                        case "DOWNLOADING":
                            item.StatusMessage = "Downloading...";
                            item.Progress = 40;
                            ActivityService.Instance.UpdateTask(updateId, ActivityTaskStatus.Running, "Downloading update...", 40);
                            break;
                        case "INSTALLING":
                            item.StatusMessage = "Installing...";
                            item.Progress = 80;
                            ActivityService.Instance.UpdateTask(updateId, ActivityTaskStatus.Running, "Installing update...", 80);
                            break;
                        case "SUCCESS":
                            item.StatusMessage = "Installed Successfully";
                            item.Progress = 100;
                            item.IsBusy = false;
                            item.IsSuccess = true;
                            item.IsFailed = false;
                            ActivityService.Instance.UpdateTask(updateId, ActivityTaskStatus.Completed, "Installed Successfully", 100);
                            break;
                        case "FAILED":
                            item.StatusMessage = "Failed";
                            item.Progress = 100;
                            item.IsBusy = false;
                            item.IsSuccess = false;
                            item.IsFailed = true;
                            item.ErrorMessage = detail;
                            ActivityService.Instance.UpdateTask(updateId, ActivityTaskStatus.Failed, $"Failed: {detail}", 100);
                            break;
                    }
                });
            },
            onCompleted: needsReboot =>
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    RebootRequired = needsReboot;
                });
            },
            cancellationToken: _updateCts.Token);

        IsInstalling = false;

        var succeeded = selected.Count(u => u.IsSuccess);
        var failed = selected.Count(u => u.IsFailed);

        HasSummary = true;
        if (RebootRequired)
        {
            SummaryMessage = $"🎉 Completed: {succeeded} update(s) installed, {failed} failed. ⚠️ A system reboot is required to finish applying changes.";
            StatusText = "Installation finished. System restart required.";
        }
        else
        {
            SummaryMessage = $"🎉 Completed: {succeeded} update(s) installed, {failed} failed.";
            StatusText = "Windows update installation completed.";
        }
        ActivityService.Instance.CompleteSession(StatusText);
    }

    [RelayCommand]
    private async Task RebootComputer()
    {
        AddLog("Initiating system restart...");
        await _powerShellService.ExecuteCommandAsync("Restart-Computer -Force");
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredUpdates.Clear();
        var filter = SearchFilter?.Trim() ?? string.Empty;

        var results = string.IsNullOrWhiteSpace(filter)
            ? AvailableUpdates
            : AvailableUpdates.Where(u =>
                u.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                u.KBArticle.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                u.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                u.Severity.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var item in results)
        {
            FilteredUpdates.Add(item);
        }
    }

    private void UpdateSelectionSummary()
    {
        var sel = AvailableUpdates.Where(u => u.IsSelected).ToList();
        SelectedCount = sel.Count;
        TotalSelectedSizeMB = Math.Round(sel.Sum(u => u.SizeMB), 1);
    }

    private void AddLog(string msg)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (Logs.Count > 100)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
            ActivityService.Instance.AddLog(msg);
        });
    }
}
