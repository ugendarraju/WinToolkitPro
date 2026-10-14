using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly SystemCleanerService _cleanerService;
    private CancellationTokenSource? _cleanerCts;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusText = "Ready to analyze system junk & caches";

    [ObservableProperty]
    private long _totalJunkBytes;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private bool _hasSummary;

    public ObservableCollection<CleanerTargetItem> Targets { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public string TotalJunkFormatted => SystemCleanerService.FormatBytes(TotalJunkBytes);

    public CleanerViewModel(SystemCleanerService cleanerService)
    {
        _cleanerService = cleanerService;
        LoadTargets();
    }

    private void LoadTargets()
    {
        Targets.Clear();
        var defaults = _cleanerService.GetDefaultTargets();
        foreach (var t in defaults)
        {
            t.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CleanerTargetItem.IsSelected))
                {
                    UpdateSummaryMetrics();
                }
            };
            Targets.Add(t);
        }
        UpdateSummaryMetrics();
    }

    [RelayCommand]
    private async Task Analyze()
    {
        IsAnalyzing = true;
        HasSummary = false;
        StatusText = "Analyzing junk files, caches, and logs...";
        _cleanerCts = new CancellationTokenSource();

        await _cleanerService.AnalyzeTargetsAsync(Targets, AddLog, _cleanerCts.Token);

        UpdateSummaryMetrics();
        IsAnalyzing = false;
        StatusText = TotalJunkBytes > 0 
            ? $"Analysis complete: {TotalJunkFormatted} of junk found."
            : "Analysis complete: Your system is already squeaky clean!";
    }

    [RelayCommand]
    private async Task Clean()
    {
        var selected = Targets.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "Please select at least one item to clean.";
            return;
        }

        IsCleaning = true;
        HasSummary = false;
        SummaryMessage = string.Empty;
        StatusText = "Cleaning selected items...";
        _cleanerCts = new CancellationTokenSource();

        var taskItems = selected.Select(t => new ActivityTaskItem
        {
            Id = t.Id,
            Name = t.Name,
            Category = t.Category,
            StatusMessage = "Queued..."
        }).ToList();

        ActivityService.Instance.StartSession("System Cleaner", taskItems);

        void OnTargetPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (s is CleanerTargetItem item)
            {
                if (item.IsBusy)
                {
                    ActivityService.Instance.UpdateTask(item.Id, ActivityTaskStatus.Running, "Purging temporary files...", 50);
                }
                else if (item.Status == "Cleaned")
                {
                    ActivityService.Instance.UpdateTask(item.Id, ActivityTaskStatus.Completed, "Cleaned", 100);
                }
            }
        }

        foreach (var t in selected)
        {
            t.PropertyChanged += OnTargetPropertyChanged;
        }

        try
        {
            var cleaned = await _cleanerService.CleanTargetsAsync(selected, AddLog, _cleanerCts.Token);

            UpdateSummaryMetrics();
            IsCleaning = false;
            HasSummary = true;
            SummaryMessage = $"🎉 Successfully freed up {SystemCleanerService.FormatBytes(cleaned)} of disk space!";
            StatusText = "Cleaning routine completed.";
            ActivityService.Instance.CompleteSession(SummaryMessage);
        }
        finally
        {
            foreach (var t in selected)
            {
                t.PropertyChanged -= OnTargetPropertyChanged;
            }
            IsCleaning = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in Targets) t.IsSelected = true;
        UpdateSummaryMetrics();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var t in Targets) t.IsSelected = false;
        UpdateSummaryMetrics();
    }

    private void UpdateSummaryMetrics()
    {
        var sel = Targets.Where(t => t.IsSelected).ToList();
        SelectedCount = sel.Count;
        TotalJunkBytes = sel.Sum(t => t.BytesFound);
        OnPropertyChanged(nameof(TotalJunkFormatted));
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
