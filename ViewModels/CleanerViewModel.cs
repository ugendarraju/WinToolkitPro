using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly SystemCleanerService _cleanerService;
    private readonly RegistryCleanerService _registryCleanerService;
    private CancellationTokenSource? _cleanerCts;
    private CancellationTokenSource? _regCleanerCts;

    [ObservableProperty]
    private int _selectedSubTabIndex = 0; // 0 = Filesystem Junk & Cache, 1 = Registry Integrity & Cleaner

    // ================= Filesystem Cleaner Properties =================
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

    // ================= Registry Cleaner Properties =================
    [ObservableProperty]
    private bool _isScanningRegistry;

    [ObservableProperty]
    private bool _isFixingRegistry;

    [ObservableProperty]
    private string _registryStatusText = "Ready to scan for broken, stale, and orphaned registry keys";

    [ObservableProperty]
    private int _selectedRegistryIssueCount;

    [ObservableProperty]
    private bool _backupRegistryBeforeFix = true;

    [ObservableProperty]
    private bool _hasRegistrySummary;

    [ObservableProperty]
    private string _registrySummaryMessage = string.Empty;

    public ObservableCollection<RegistryIssueItem> RegistryIssues { get; } = new();

    public ObservableCollection<string> Logs { get; } = new();

    public string TotalJunkFormatted => SystemCleanerService.FormatBytes(TotalJunkBytes);

    public CleanerViewModel(SystemCleanerService cleanerService, RegistryCleanerService registryCleanerService)
    {
        _cleanerService = cleanerService;
        _registryCleanerService = registryCleanerService;
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

    // ================= Filesystem Junk Commands =================
    [RelayCommand]
    private async Task Analyze()
    {
        IsAnalyzing = true;
        HasSummary = false;
        StatusText = "Analyzing junk files, caches, and logs...";
        _cleanerCts = new CancellationTokenSource();

        try
        {
            await _cleanerService.AnalyzeTargetsAsync(Targets, AddLog, _cleanerCts.Token);
            UpdateSummaryMetrics();
            StatusText = $"Analysis complete. Found {TotalJunkFormatted} across selected targets.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Analysis error: {ex.Message}";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private async Task Clean()
    {
        var selected = Targets.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "No categories selected for cleaning.";
            return;
        }

        IsCleaning = true;
        HasSummary = false;
        StatusText = "Purging selected caches and temporary files...";
        _cleanerCts = new CancellationTokenSource();

        void OnTargetPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CleanerTargetItem.BytesFound))
            {
                UpdateSummaryMetrics();
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

    // ================= Registry Cleaner Commands =================
    [RelayCommand]
    public async Task ScanRegistryAsync()
    {
        IsScanningRegistry = true;
        HasRegistrySummary = false;
        RegistryStatusText = "Scanning registry for missing DLLs, invalid App Paths, and stale entries...";
        AddLog("Scanning Windows Registry integrity...");
        _regCleanerCts = new CancellationTokenSource();

        try
        {
            var issues = await _registryCleanerService.ScanRegistryIssuesAsync(AddLog, _regCleanerCts.Token);

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                RegistryIssues.Clear();
                foreach (var issue in issues)
                {
                    issue.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(RegistryIssueItem.IsSelected))
                        {
                            UpdateRegistrySelectedCount();
                        }
                    };
                    RegistryIssues.Add(issue);
                }
                UpdateRegistrySelectedCount();
                RegistryStatusText = $"Scan complete. Discovered {RegistryIssues.Count} registry issue(s).";
            });
        }
        catch (Exception ex)
        {
            RegistryStatusText = $"Registry scan failed: {ex.Message}";
            AddLog($"Registry error: {ex.Message}");
        }
        finally
        {
            IsScanningRegistry = false;
        }
    }

    [RelayCommand]
    public async Task FixSelectedRegistryIssuesAsync()
    {
        var selected = RegistryIssues.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Please select at least one registry issue to fix.", "Registry Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Are you sure you want to clean and resolve {selected.Count} selected registry issue(s)?",
            "Confirm Registry Cleaning",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsFixingRegistry = true;
        RegistryStatusText = $"Cleaning {selected.Count} registry issue(s)...";

        try
        {
            if (BackupRegistryBeforeFix)
            {
                var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinToolkit", "RegistryBackups");
                RegistryStatusText = "Creating registry safety backup...";
                await _registryCleanerService.BackupRegistryBranchAsync(backupDir, AddLog);
            }

            var fixedCount = await _registryCleanerService.FixRegistryIssuesAsync(selected, AddLog);

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Remove fixed items from the active list
                foreach (var f in selected.Where(i => i.Status == "Fixed").ToList())
                {
                    RegistryIssues.Remove(f);
                }
                UpdateRegistrySelectedCount();

                HasRegistrySummary = true;
                RegistrySummaryMessage = $"🎉 Successfully cleaned and optimized {fixedCount} registry key(s)!";
                RegistryStatusText = "Registry optimization completed.";
            });

            MessageBox.Show(
                $"Registry cleaning complete!\n\nFixed and optimized: {fixedCount} issue(s).",
                "Registry Cleaner",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RegistryStatusText = $"Error cleaning registry: {ex.Message}";
        }
        finally
        {
            IsFixingRegistry = false;
        }
    }

    [RelayCommand]
    private void SelectAllRegistry()
    {
        foreach (var i in RegistryIssues) i.IsSelected = true;
        UpdateRegistrySelectedCount();
    }

    [RelayCommand]
    private void ClearAllRegistry()
    {
        foreach (var i in RegistryIssues) i.IsSelected = false;
        UpdateRegistrySelectedCount();
    }

    private void UpdateRegistrySelectedCount()
    {
        SelectedRegistryIssueCount = RegistryIssues.Count(i => i.IsSelected);
    }

    private void AddLog(string msg)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
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
