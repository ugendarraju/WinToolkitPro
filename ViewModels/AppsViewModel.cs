using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

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
    private string _wingetStatusBadge = "WinGet: Checking...";

    [ObservableProperty]
    private string _wingetBadgeBackground = "#1e293b";

    [ObservableProperty]
    private string _wingetBadgeBorderBrush = "#475569";

    [ObservableProperty]
    private string _wingetBadgeForeground = "#94a3b8";

    [ObservableProperty]
    private bool _isWingetBootstrapping;

    [ObservableProperty]
    private double _wingetBootstrapProgress;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private bool _isActivityPanelOpen;

    public string ArchitectureNotice => OsEnvironmentService.Instance.Is64BitOperatingSystem 
        ? "64-bit Mode" 
        : "32-bit (x86) Mode Active";

    public ObservableCollection<AppItem> ActiveTasks { get; } = new();
    public ObservableCollection<AppItem> AllApps { get; } = new();
    public ObservableCollection<AppItem> FilteredApps { get; } = new();
    public ObservableCollection<AppCategoryGroup> GroupedApps { get; } = new();
    public ObservableCollection<CategoryFilterItem> FilterChips { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public static (string Icon, string Description) GetCategoryMeta(string category) => category switch
    {
        "Web Browsers" => ("🌐", "Fast, modern web browsers for everyday surfing and web productivity."),
        "Messaging" => ("💬", "Chat, collaboration, instant messaging, and team conferencing."),
        "Media" => ("🎬", "Audio and video players, streaming suites, and media encoders."),
        ".NET" => ("⚙️", ".NET Desktop Runtimes, ASP.NET Core, and SDK environments."),
        "Java" => ("☕", "Oracle JRE and Eclipse Temurin Java runtimes for Java software."),
        "Imaging" => ("🎨", "Photo editing, vector design, screen capture, and 3D creation."),
        "3D Printing" => ("🖨️", "Slicing software and 3D printing preparation suites."),
        "Documents" => ("📄", "Office productivity suites, PDF readers, and document editors."),
        "Security" => ("🛡️", "Antivirus suites, malware removal, and endpoint protection."),
        "Compression" => ("🗜️", "Archive managers, zip tools, and file compression utilities."),
        "File Sharing" => ("🔄", "Local file transfer and peer-to-peer downloading utilities."),
        "Online Storage" => ("☁️", "Cloud synchronization and desktop cloud drive clients."),
        "Developer Tools" => ("💻", "IDEs, command-line shells, Git version control, and API testing."),
        "VC++ Redistributables" => ("🧩", "Microsoft Visual C++ universal runtime libraries."),
        "Remote Desktop" => ("🖥️", "Remote desktop control, assistance, and VNC utilities."),
        "Hardware & Diagnostics" => ("🔍", "System hardware sensors, CPU/GPU identification, and benchmarking."),
        "System Tools" => ("🧰", "Uninstallers, power tools, shell enhancers, and maintenance utilities."),
        "File & Disk Tools" => ("💾", "Disk space analyzers, rapid file searchers, and drive tools."),
        "Utilities" => ("🛠️", "Handy everyday PC utilities and workflow boosters."),
        _ => ("📦", "Popular desktop software and utilities.")
    };

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
        WingetStatusBadge = "WinGet: Checking...";
        WingetBadgeBackground = "#1e293b";
        WingetBadgeBorderBrush = "#475569";
        WingetBadgeForeground = "#94a3b8";

        var wingetVer = await _wingetService.GetWingetVersionAsync();
        if (!string.IsNullOrEmpty(wingetVer))
        {
            IsWingetAvailable = true;
            IsWingetBootstrapping = false;
            WingetStatusBadge = $"WinGet Ready ({wingetVer})";
            WingetBadgeBackground = "#064e3b";
            WingetBadgeBorderBrush = "#10b981";
            WingetBadgeForeground = "#34d399";
            StatusText = $"WinGet {wingetVer} detected and ready.";
            ActivityService.Instance.AddLog($"WinGet package manager verified: {wingetVer}");
        }
        else
        {
            IsWingetAvailable = false;
            IsWingetBootstrapping = true;
            WingetBootstrapProgress = 0;
            WingetStatusBadge = "WinGet: Not Present (Auto-Installing...)";
            WingetBadgeBackground = "#451a03";
            WingetBadgeBorderBrush = "#f59e0b";
            WingetBadgeForeground = "#fbbf24";
            StatusText = "WinGet not found on system. Installing Windows Package Manager automatically...";
            ActivityService.Instance.AddLog("WinGet not found on system. Starting automatic installation in background...");

            _ = AutoInstallWingetAsync();
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
            "Compression", "File Sharing", "Other",
            "Online Storage", "Developer Tools", "VC++ Redistributables",
            "Remote Desktop", "Hardware & Diagnostics", "System Tools", "File & Disk Tools", "Utilities"
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
            if (!OsEnvironmentService.Instance.IsAppSupported(app)) continue;

            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppItem.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            AllApps.Add(app);
        }

        // Build Navigation Filter Chips
        FilterChips.Clear();
        FilterChips.Add(new CategoryFilterItem
        {
            Name = "All",
            DisplayName = "All Sections",
            Icon = "🌟",
            Count = AllApps.Count,
            IsSelected = SelectedCategory.Equals("All", StringComparison.OrdinalIgnoreCase)
        });

        foreach (var cat in Categories)
        {
            if (cat.Equals("All", StringComparison.OrdinalIgnoreCase)) continue;
            var meta = GetCategoryMeta(cat);
            var catCount = AllApps.Count(a => a.Category.Equals(cat, StringComparison.OrdinalIgnoreCase));
            if (catCount > 0)
            {
                FilterChips.Add(new CategoryFilterItem
                {
                    Name = cat,
                    DisplayName = cat,
                    Icon = meta.Icon,
                    Count = catCount,
                    IsSelected = SelectedCategory.Equals(cat, StringComparison.OrdinalIgnoreCase)
                });
            }
        }

        ApplyFilter();
        UpdateSelectedCount();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    [RelayCommand]
    public void SelectCategoryFilter(string categoryName)
    {
        SelectedCategory = categoryName;
    }

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
            var meta = GetCategoryMeta(group.Key);
            var grp = new AppCategoryGroup
            {
                CategoryName = group.Key,
                Icon = meta.Icon,
                Description = meta.Description,
                IsExpanded = true
            };
            foreach (var app in group)
            {
                grp.AddApp(app);
            }
            GroupedApps.Add(grp);
        }

        // Update chips selection highlights
        foreach (var chip in FilterChips)
        {
            chip.IsSelected = chip.Name.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllApps.Count(a => a.IsSelected);
        foreach (var group in GroupedApps)
        {
            group.UpdateCounts();
        }
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
    public void ExpandAll()
    {
        foreach (var group in GroupedApps)
        {
            group.IsExpanded = true;
        }
    }

    [RelayCommand]
    public void CollapseAll()
    {
        foreach (var group in GroupedApps)
        {
            group.IsExpanded = false;
        }
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

        // Verify if any selected apps require WinGet and bootstrap it if missing (e.g. fresh Windows or Windows Sandbox)
        bool hasWingetApps = selectedApps.Any(a =>
            !a.Id.Equals("chrome", StringComparison.OrdinalIgnoreCase) &&
            !a.WingetId.Equals("Google.Chrome", StringComparison.OrdinalIgnoreCase) &&
            !a.Id.Equals("vcredist_aio", StringComparison.OrdinalIgnoreCase) &&
            !a.InstallerType.Equals("vcredist_aio", StringComparison.OrdinalIgnoreCase) &&
            !a.InstallerType.Equals("office_c2r", StringComparison.OrdinalIgnoreCase) &&
            !a.InstallerType.Equals("zip_bat", StringComparison.OrdinalIgnoreCase) &&
            !a.InstallerType.Equals("exe_download", StringComparison.OrdinalIgnoreCase));

        if (hasWingetApps && !IsWingetAvailable)
        {
            AddLog("Windows Package Manager (WinGet) is not detected on this system. Bootstrapping WinGet...");
            StatusText = "Installing WinGet package manager...";
            IsWingetAvailable = await _wingetService.EnsureWingetInstalledAsync(AddLog);
        }

        // Semaphores:
        // 1. Up to 3 simultaneous background downloads
        var downloadSemaphore = new SemaphoreSlim(3, 3);
        // 2. Up to 2 concurrent non-MSI installs, but strictly 1 MSI install at a time (Windows 1618 mutex lock)
        var generalInstallSemaphore = new SemaphoreSlim(2, 2);
        var msiLock = new SemaphoreSlim(1, 1);

        try
        {
            var tasks = selectedApps.Select(async app =>
            {
                if (ct.IsCancellationRequested) return;

                void OnAppPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(AppItem.Progress) || 
                        e.PropertyName == nameof(AppItem.StatusMessage) || 
                        e.PropertyName == nameof(AppItem.Status))
                    {
                        var taskStatus = app.Status switch
                        {
                            InstallStatus.Installed => ActivityTaskStatus.Completed,
                            InstallStatus.Failed => ActivityTaskStatus.Failed,
                            InstallStatus.Queued => ActivityTaskStatus.Queued,
                            _ => ActivityTaskStatus.Running
                        };
                        ActivityService.Instance.UpdateTask(app.Id, taskStatus, app.StatusMessage, app.Progress);
                    }
                }

                app.PropertyChanged += OnAppPropertyChanged;

                try
                {
                    // Step 1: Background Pre-Download
                    await downloadSemaphore.WaitAsync(ct);
                    try
                    {
                        if (ct.IsCancellationRequested) return;
                        AddLog($"[{app.Name}] Downloading in background...");
                        ActivityService.Instance.UpdateTask(app.Id, ActivityTaskStatus.Running, "Downloading...", app.Progress > 0 ? app.Progress : 5);
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
                }
                finally
                {
                    app.PropertyChanged -= OnAppPropertyChanged;
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

    [RelayCommand]
    public async Task RetryInstallWingetAsync()
    {
        if (IsWingetBootstrapping) return;
        IsWingetBootstrapping = true;
        await AutoInstallWingetAsync();
    }

    private async Task AutoInstallWingetAsync()
    {
        var task = new ActivityTaskItem
        {
            Id = "winget-bootstrap",
            Name = "Windows Package Manager (WinGet)",
            Category = "System Setup",
            Status = ActivityTaskStatus.Running,
            StatusMessage = "Starting WinGet bootstrap...",
            Progress = 0
        };
        ActivityService.Instance.StartSession("WinGet Setup", new[] { task });
        ActivityService.Instance.OpenDrawer();

        var success = await _wingetService.EnsureWingetInstalledAsync(
            onProgress: (status, progress) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    StatusText = status;
                    WingetBootstrapProgress = progress;
                    WingetStatusBadge = $"WinGet: Installing ({progress:F0}%)...";
                    ActivityService.Instance.UpdateTask("winget-bootstrap", ActivityTaskStatus.Running, status, progress);
                });
            },
            onLog: (msg) =>
            {
                AddLog(msg);
                ActivityService.Instance.AddLog(msg);
            });

        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            IsWingetBootstrapping = false;
            if (success)
            {
                IsWingetAvailable = true;
                _ = Task.Run(async () =>
                {
                    var ver = await _wingetService.GetWingetVersionAsync() ?? "Ready";
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        WingetStatusBadge = $"WinGet Ready ({ver})";
                        WingetBadgeBackground = "#064e3b";
                        WingetBadgeBorderBrush = "#10b981";
                        WingetBadgeForeground = "#34d399";
                        StatusText = $"WinGet {ver} installed successfully.";
                    });
                });
                ActivityService.Instance.UpdateTask("winget-bootstrap", ActivityTaskStatus.Completed, "WinGet successfully installed and ready", 100);
            }
            else
            {
                IsWingetAvailable = false;
                WingetStatusBadge = "WinGet: Incomplete (Direct Mode Active)";
                WingetBadgeBackground = "#451a03";
                WingetBadgeBorderBrush = "#d97706";
                WingetBadgeForeground = "#fcd34d";
                StatusText = "WinGet auto-install could not complete. Direct vendor installers remain active.";
                ActivityService.Instance.UpdateTask("winget-bootstrap", ActivityTaskStatus.Failed, "WinGet setup incomplete - fallback active", 0);
            }
        });
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
