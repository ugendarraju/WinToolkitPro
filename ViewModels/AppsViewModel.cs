using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public class AppCategoryGroup
{
    public required string CategoryName { get; set; }
    public ObservableCollection<AppItem> Apps { get; set; } = new();
}

public partial class AppsViewModel : ObservableObject
{
    private readonly WingetService _wingetService;
    private readonly CatalogService _catalogService;
    private CancellationTokenSource? _installCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isWingetAvailable = true;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private bool _isActivityPanelOpen;

    public ObservableCollection<AppItem> ActiveTasks { get; } = new();
    public ObservableCollection<AppItem> AllApps { get; } = new();
    public ObservableCollection<AppItem> FilteredApps { get; } = new();
    public ObservableCollection<AppCategoryGroup> GroupedApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    [RelayCommand]
    private void ToggleActivityPanel()
    {
        ActivityService.Instance.ToggleDrawer();
        IsActivityPanelOpen = ActivityService.Instance.IsOpen;
    }

    [RelayCommand]
    private void CloseActivityPanel()
    {
        ActivityService.Instance.CloseDrawer();
        IsActivityPanelOpen = false;
    }

    public AppsViewModel(WingetService wingetService, CatalogService catalogService)
    {
        _wingetService = wingetService;
        _catalogService = catalogService;
    }

    public async Task InitializeAsync()
    {
        IsWingetAvailable = await _wingetService.IsWingetAvailableAsync();
        if (!IsWingetAvailable)
        {
            StatusText = "Notice: winget is not installed or detected in PATH.";
        }

        var apps = await _catalogService.LoadAppsAsync();
        AllApps.Clear();
        Categories.Clear();
        Categories.Add("All");

        // Fixed category order matching standard installer sections
        var categoryOrder = new List<string>
        {
            "Web Browsers", "Messaging", "Media", ".NET", "Java",
            "Imaging", "3D Printing", "Documents", "Security",
            "Utilities", "Compression", "File Sharing", "Other",
            "Online Storage", "Developer Tools", "VC++ Redistributables"
        };

        var loadedCategories = apps.Select(a => a.Category).Distinct().ToList();
        foreach (var cat in categoryOrder)
        {
            if (loadedCategories.Contains(cat) && !Categories.Contains(cat))
            {
                Categories.Add(cat);
            }
        }
        foreach (var cat in loadedCategories)
        {
            if (!Categories.Contains(cat))
            {
                Categories.Add(cat);
            }
        }

        foreach (var app in apps)
        {
            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppItem.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            AllApps.Add(app);
        }

        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        GroupedApps.Clear();

        var query = SearchQuery?.Trim() ?? string.Empty;

        var filtered = AllApps.Where(a =>
            (SelectedCategory == "All" || a.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query) ||
             a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             a.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             a.WingetId.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var item in filtered)
        {
            FilteredApps.Add(item);
        }

        // Group by category while preserving clean section order
        var groups = filtered
            .GroupBy(a => a.Category)
            .OrderBy(g => Categories.IndexOf(g.Key) >= 0 ? Categories.IndexOf(g.Key) : 999);

        foreach (var group in groups)
        {
            var grp = new AppCategoryGroup { CategoryName = group.Key };
            foreach (var app in group)
            {
                grp.Apps.Add(app);
            }
            GroupedApps.Add(grp);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllApps.Count(a => a.IsSelected);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var app in FilteredApps)
        {
            app.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var app in AllApps)
        {
            app.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task InstallSelected()
    {
        var selectedApps = AllApps.Where(a => a.IsSelected).ToList();
        if (selectedApps.Count == 0) return;

        IsInstalling = true;
        _installCts = new CancellationTokenSource();
        var ct = _installCts.Token;

        var total = selectedApps.Count;
        var completedCount = 0;

        ActiveTasks.Clear();
        var taskItems = new List<ActivityTaskItem>();
        foreach (var app in selectedApps)
        {
            ActiveTasks.Add(app);
            app.Status = InstallStatus.Queued;
            app.StatusMessage = "Waiting in queue...";
            app.Progress = 0;

            taskItems.Add(new ActivityTaskItem
            {
                Id = app.Id,
                Name = app.Name,
                Category = app.Category,
                Status = ActivityTaskStatus.Queued,
                StatusMessage = "Waiting in queue...",
                Progress = 0
            });
        }
        ActivityService.Instance.StartSession("Software Installation", taskItems);
        IsActivityPanelOpen = true;

        AddLog($"Starting background download & installation pipeline for {total} app(s)...");

        // Semaphores:
        // 1. Up to 3 simultaneous background downloads
        var downloadSemaphore = new SemaphoreSlim(3, 3);
        // 2. Up to 2 concurrent non-MSI installs, but strictly 1 MSI install at a time (Windows 1618 mutex lock)
        var generalInstallSemaphore = new SemaphoreSlim(2, 2);
        var msiLock = new SemaphoreSlim(1, 1);

        try
        {
            // Launch simultaneous background download tasks
            // Each app starts downloading in background as downloadSemaphore allows.
            // When downloaded, it acquires installation locks and installs.
            var tasks = selectedApps.Select(async app =>
            {
                if (ct.IsCancellationRequested) return;

                // Step 1: Background Pre-Download
                await downloadSemaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested) return;
                    AddLog($"[{app.Name}] Downloading in background...");
                    ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Running, "Downloading...", 30);
                    await _wingetService.PreDownloadAppAsync(app, AddLog, ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    AddLog($"[{app.Name}] Download notice: {ex.Message}");
                }
                finally
                {
                    downloadSemaphore.Release();
                }

                if (ct.IsCancellationRequested) return;

                // Step 2: Installation Queue
                var isMsi = WingetService.IsMsiPackage(app);
                app.Status = InstallStatus.Queued;
                app.StatusMessage = isMsi ? "Waiting for MSI installer slot..." : "Waiting for installation slot...";
                ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Running, app.StatusMessage, 50);

                await generalInstallSemaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested) return;

                    if (isMsi)
                    {
                        // Acquire exclusive MSI lock so multiple MSIs don't collide with Error 1618
                        await msiLock.WaitAsync(ct);
                    }

                    try
                    {
                        if (ct.IsCancellationRequested) return;

                        StatusText = $"Installing {app.Name}...";
                        AddLog($"[{app.Name}] Starting installation...");
                        ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Running, "Installing package...", 75);
                        await _wingetService.InstallAppAsync(app, AddLog, ct);

                        if (app.Status == InstallStatus.Installed)
                        {
                            ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Completed, "Installed", 100);
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                app.IsSelected = false;
                                UpdateSelectedCount();
                            });
                        }
                        else
                        {
                            ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Failed, app.StatusMessage, 100);
                        }
                    }
                    finally
                    {
                        if (isMsi)
                        {
                            msiLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    generalInstallSemaphore.Release();
                    var done = Interlocked.Increment(ref completedCount);
                    OverallProgress = ((double)done / total) * 100.0;
                    StatusText = $"Progress: {done}/{total} app(s) processed";
                    ActivityService.Instance.UpdateOverallProgress(OverallProgress, StatusText);
                }
            });

            await Task.WhenAll(tasks);

            StatusText = ct.IsCancellationRequested ? "Installation cancelled." : "All selected apps processed!";
            AddLog(StatusText);
            ActivityService.Instance.CompleteSession(StatusText);
        }
        finally
        {
            await _wingetService.CleanTempFilesAsync(AddLog);
            IsInstalling = false;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var app in selectedApps)
                {
                    if (app.Status == InstallStatus.Installed)
                    {
                        app.IsSelected = false;
                    }
                }
                UpdateSelectedCount();
            });
        }
    }

    [RelayCommand]
    private async Task CleanTempCache()
    {
        await _wingetService.CleanTempFilesAsync(AddLog);
    }

    [RelayCommand]
    private void CancelInstallation()
    {
        _installCts?.Cancel();
        StatusText = "Cancelling...";
        AddLog("Cancelling ongoing installations...");
    }

    private void AddLog(string message)
    {
        ActivityService.Instance.AddLog(message);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
