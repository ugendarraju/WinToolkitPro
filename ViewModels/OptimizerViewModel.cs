using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class OptimizerViewModel : ObservableObject
{
    private readonly TweakService _tweakService;
    private readonly CatalogService _catalogService;
    private CancellationTokenSource? _tweakCts;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _createRestorePointFirst = true;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private double _overallProgress;

    public ObservableCollection<TweakItem> AllTweaks { get; } = new();
    public ObservableCollection<TweakItem> FilteredTweaks { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public OptimizerViewModel(TweakService tweakService, CatalogService catalogService)
    {
        _tweakService = tweakService;
        _catalogService = catalogService;
    }

    public async Task InitializeAsync()
    {
        var tweaks = await _catalogService.LoadTweaksAsync();
        AllTweaks.Clear();
        Categories.Clear();
        Categories.Add("All");

        var distinctCategories = tweaks.Select(t => t.Category).Distinct();
        foreach (var cat in distinctCategories)
        {
            Categories.Add(cat);
        }

        foreach (var tweak in tweaks)
        {
            tweak.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(TweakItem.IsChecked))
                {
                    UpdateSelectedCount();
                }
            };
            AllTweaks.Add(tweak);
        }

        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredTweaks.Clear();
        var filtered = AllTweaks.Where(t =>
            SelectedCategory == "All" || t.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));

        foreach (var item in filtered)
        {
            FilteredTweaks.Add(item);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllTweaks.Count(t => t.IsChecked);
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var tweak in AllTweaks)
        {
            tweak.IsChecked = tweak.Impact == "Recommended";
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var tweak in FilteredTweaks)
        {
            tweak.IsChecked = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var tweak in AllTweaks)
        {
            tweak.IsChecked = false;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task ApplySelected()
    {
        var selectedTweaks = AllTweaks.Where(t => t.IsChecked).ToList();
        if (selectedTweaks.Count == 0) return;

        IsApplying = true;
        _tweakCts = new CancellationTokenSource();
        var ct = _tweakCts.Token;

        var taskItems = new List<ActivityTaskItem>();
        if (CreateRestorePointFirst)
        {
            taskItems.Add(new ActivityTaskItem
            {
                Id = "restore_point",
                Name = "System Restore Point",
                Category = "Safety Backup",
                StatusMessage = "Queued..."
            });
        }
        taskItems.AddRange(selectedTweaks.Select(t => new ActivityTaskItem
        {
            Id = t.Id,
            Name = t.Title,
            Category = t.Category,
            StatusMessage = "Queued..."
        }));

        ActivityService.Instance.StartSession("OS Optimization", taskItems);

        if (CreateRestorePointFirst)
        {
            StatusText = "Creating System Restore Point...";
            ActivityService.Instance.UpdateTask("restore_point", ActivityTaskStatus.Running, "Creating Windows System Restore Point...", 40);
            AddLog("Creating Windows System Restore Point 'WinToolkit Pre-Optimization'...");
            var restoreSuccess = await _tweakService.CreateSystemRestorePointAsync("WinToolkit Pre-Optimization");
            AddLog(restoreSuccess ? "Restore Point successfully created." : "Note: Could not create restore point (requires Admin/enabled VSS). Proceeding with tweaks.");
            ActivityService.Instance.UpdateTask("restore_point", restoreSuccess ? ActivityTaskStatus.Completed : ActivityTaskStatus.Failed,
                restoreSuccess ? "Restore Point created" : "Skipped/Failed", 100);
        }

        var total = selectedTweaks.Count;
        var current = 0;

        foreach (var tweak in selectedTweaks)
        {
            if (ct.IsCancellationRequested) break;

            current++;
            StatusText = $"Applying {tweak.Title} ({current}/{total})...";
            OverallProgress = ((double)(current - 1) / total) * 100.0;
            ActivityService.Instance.UpdateOverallProgress(OverallProgress);
            ActivityService.Instance.UpdateTask(tweak.Id, ActivityTaskStatus.Running, "Applying tweak...", 50);

            await _tweakService.ApplyTweakAsync(tweak, AddLog, ct);

            ActivityService.Instance.UpdateTask(tweak.Id, ActivityTaskStatus.Completed, "Applied", 100);
            OverallProgress = ((double)current / total) * 100.0;
            ActivityService.Instance.UpdateOverallProgress(OverallProgress);
        }

        IsApplying = false;
        StatusText = ct.IsCancellationRequested ? "Optimization stopped." : "All selected optimizations applied successfully!";
        AddLog(StatusText);
        ActivityService.Instance.CompleteSession(StatusText);
    }

    [RelayCommand]
    private async Task RevertSelected()
    {
        var selectedTweaks = AllTweaks.Where(t => t.IsChecked).ToList();
        if (selectedTweaks.Count == 0) return;

        IsApplying = true;
        _tweakCts = new CancellationTokenSource();
        var ct = _tweakCts.Token;

        var taskItems = selectedTweaks.Select(t => new ActivityTaskItem
        {
            Id = t.Id,
            Name = t.Title,
            Category = t.Category,
            StatusMessage = "Queued..."
        }).ToList();

        ActivityService.Instance.StartSession("Revert OS Optimizations", taskItems);

        var total = selectedTweaks.Count;
        var current = 0;

        foreach (var tweak in selectedTweaks)
        {
            if (ct.IsCancellationRequested) break;

            current++;
            StatusText = $"Reverting {tweak.Title} ({current}/{total})...";
            ActivityService.Instance.UpdateTask(tweak.Id, ActivityTaskStatus.Running, "Reverting to default...", 50);
            await _tweakService.RevertTweakAsync(tweak, AddLog, ct);
            ActivityService.Instance.UpdateTask(tweak.Id, ActivityTaskStatus.Completed, "Reverted", 100);
            ActivityService.Instance.UpdateOverallProgress(((double)current / total) * 100.0);
        }

        IsApplying = false;
        StatusText = "Selected optimizations reverted to default.";
        AddLog(StatusText);
        ActivityService.Instance.CompleteSession(StatusText);
    }

    [RelayCommand]
    private async Task CreateManualRestorePoint()
    {
        StatusText = "Creating System Restore Point...";
        AddLog("Initiating manual System Restore Point creation...");
        var taskItem = new ActivityTaskItem
        {
            Id = "manual_restore",
            Name = "System Restore Point",
            Category = "Safety Backup",
            Status = ActivityTaskStatus.Running,
            StatusMessage = "Creating checkpoint..."
        };
        ActivityService.Instance.StartSession("Safety Checkpoint", new List<ActivityTaskItem> { taskItem });

        var success = await _tweakService.CreateSystemRestorePointAsync("WinToolkit Manual Checkpoint");
        StatusText = success ? "Restore Point created successfully!" : "Failed to create Restore Point (Run as Admin).";
        AddLog(StatusText);
        ActivityService.Instance.UpdateTask("manual_restore", success ? ActivityTaskStatus.Completed : ActivityTaskStatus.Failed, StatusText, 100);
        ActivityService.Instance.CompleteSession(StatusText);
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
            ActivityService.Instance.AddLog(message);
        });
    }
}
