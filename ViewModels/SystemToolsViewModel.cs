using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
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
    private bool _isScanningApps;

    // Bloatware tab properties
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

    // Disk Analyzer properties
    [ObservableProperty]
    private string _selectedDrive = "C:\\";

    [ObservableProperty]
    private string _currentDiskPath = "C:\\";

    [ObservableProperty]
    private bool _canNavigateUp;

    [ObservableProperty]
    private bool _isTreemapView = true;

    [ObservableProperty]
    private bool _isDiskScanning;

    // Service Filtering & Grouping Properties
    private readonly List<ServiceItem> _allServices = new();

    [ObservableProperty]
    private string _serviceSearchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedServiceCategory = "All";

    [ObservableProperty]
    private string _selectedServiceCompany = "All Companies";

    [ObservableProperty]
    private bool _hideMicrosoftServices;

    [ObservableProperty]
    private int _totalServicesCount;

    [ObservableProperty]
    private int _thirdPartyServicesCount;

    [ObservableProperty]
    private int _safeToDisableServicesCount;

    [ObservableProperty]
    private int _mandatoryServicesCount;

    [ObservableProperty]
    private int _runningServicesCount;

    public ObservableCollection<string> ServiceCompanies { get; } = new();
    public ObservableCollection<CategoryFilterItem> ServiceCategoryChips { get; } = new();

    partial void OnServiceSearchQueryChanged(string value) => ApplyServiceFilter();
    partial void OnSelectedServiceCompanyChanged(string value) => ApplyServiceFilter();
    partial void OnHideMicrosoftServicesChanged(bool value) => ApplyServiceFilter();

    // Collections
    public ObservableCollection<UwpAppItem> AllApps { get; } = new();
    public ObservableCollection<UwpAppItem> FilteredApps { get; } = new();
    public ObservableCollection<StartupItem> StartupApps { get; } = new();
    public ObservableCollection<ServiceItem> ServicesList { get; } = new();
    public ObservableCollection<DiskFolderItem> DiskFolders { get; } = new();
    public ObservableCollection<OptionalFeatureItem> OptionalFeatures { get; } = new();
    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public SystemToolsViewModel(SystemToolsService systemToolsService)
    {
        _systemToolsService = systemToolsService;

        // Populate available drives
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                AvailableDrives.Add(d.Name);
            }
            if (AvailableDrives.Count > 0)
            {
                SelectedDrive = AvailableDrives[0];
            }
        }
        catch { }
    }

    public async Task InitializeAsync()
    {
        await ScanApps();
        await LoadStartupApps();
        await LoadOptionalFeatures();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnShowBloatwareOnlyChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        void Update()
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

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            System.Windows.Application.Current.Dispatcher.Invoke(Update);
        else
            Update();
    }

    // ========================================================
    // Bloatware Uninstaller Commands
    // ========================================================
    [RelayCommand]
    public async Task ScanApps()
    {
        if (IsScanningApps) return;

        IsScanningApps = true;
        StatusMessage = "Scanning for preinstalled Windows bloatware packages...";

        void ClearApps()
        {
            AllApps.Clear();
            FilteredApps.Clear();
        }

        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
            System.Windows.Application.Current.Dispatcher.Invoke(ClearApps);
        else
            ClearApps();

        try
        {
            var apps = await _systemToolsService.ScanUwpAppsAsync(AddLog);

            void PopulateApps()
            {
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
            }

            if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
                System.Windows.Application.Current.Dispatcher.Invoke(PopulateApps);
            else
                PopulateApps();

            StatusMessage = BloatwareCount > 0
                ? $"Scan completed: {BloatwareCount} bloatware packages detected ({SelectedAppCount} selected for debloat)."
                : "Scan completed: No Windows bloatware packages detected on this system.";
        }
        catch (Exception ex)
        {
            AddLog($"Scan notice: {ex.Message}");
        }
        finally
        {
            IsScanningApps = false;
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

        var tasks = targets.Select(item => new ActivityTaskItem
        {
            Id = item.PackageFullName,
            Name = item.DisplayName,
            Category = "Bloatware Debloat",
            Status = ActivityTaskStatus.Queued,
            StatusMessage = "Queued for removal...",
            Progress = 0
        }).ToList();
        ActivityService.Instance.StartSession("Bloatware Debloat", tasks);

        AddLog($"Starting batch removal of {targets.Count} package(s)...");
        int successCount = 0;
        int processedCount = 0;

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
                ActivityService.Instance.UpdateTask(item.PackageFullName, ActivityTaskStatus.Running, "Uninstalling...", 50);
                var ok = await _systemToolsService.UninstallUwpAppAsync(item, AddLog);
                processedCount++;
                if (ok)
                {
                    successCount++;
                    ActivityService.Instance.UpdateTask(item.PackageFullName, ActivityTaskStatus.Completed, "Uninstalled", 100);
                }
                else
                {
                    ActivityService.Instance.UpdateTask(item.PackageFullName, ActivityTaskStatus.Failed, "Failed to remove", 100);
                }
                ActivityService.Instance.UpdateOverallProgress(((double)processedCount / targets.Count) * 100, StatusMessage);
            }

            StatusMessage = $"Debloat complete. Successfully removed {successCount} of {targets.Count} packages.";
            AddLog($"[Debloat] Finished: {successCount}/{targets.Count} removed.");
            ActivityService.Instance.CompleteSession(StatusMessage);
            
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

    // ========================================================
    // Startup Applications Commands
    // ========================================================
    [RelayCommand]
    public async Task LoadStartupApps()
    {
        StartupApps.Clear();
        AddLog("Scanning Windows startup applications...");

        try
        {
            var apps = await _systemToolsService.GetStartupItemsAsync();
            foreach (var item in apps)
            {
                StartupApps.Add(item);
            }
            AddLog($"Loaded {StartupApps.Count} startup programs from registry and folders.");
        }
        catch (Exception ex)
        {
            AddLog($"Error scanning startup apps: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveStartupApp(StartupItem item)
    {
        AddLog($"Removing startup item '{item.Name}'...");
        var ok = await _systemToolsService.RemoveStartupItemAsync(item);
        if (ok)
        {
            StartupApps.Remove(item);
            AddLog($"✅ Removed '{item.Name}' from startup.");
        }
        else
        {
            AddLog($"❌ Could not remove '{item.Name}'.");
        }
    }

    // ========================================================
    // Services Management Commands
    // ========================================================
    [RelayCommand]
    public async Task LoadServices()
    {
        ServicesList.Clear();
        _allServices.Clear();
        AddLog("Scanning and categorizing Windows background services...");

        try
        {
            var services = await _systemToolsService.GetServicesAsync();
            _allServices.AddRange(services);

            // Populate Vendor/Company filter list
            ServiceCompanies.Clear();
            ServiceCompanies.Add("All Companies");
            var topVendors = _allServices
                .GroupBy(s => s.Company)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key);
            foreach (var vendor in topVendors)
            {
                if (!string.IsNullOrWhiteSpace(vendor))
                    ServiceCompanies.Add(vendor);
            }
            SelectedServiceCompany = "All Companies";

            UpdateServiceChips();
            ApplyServiceFilter();

            AddLog($"Categorized {_allServices.Count} services ({ThirdPartyServicesCount} Third-Party/OEM, {SafeToDisableServicesCount} Safe to Turn Off, {MandatoryServicesCount} Mandatory Core).");
        }
        catch (Exception ex)
        {
            AddLog($"Error scanning services: {ex.Message}");
        }
    }

    [RelayCommand]
    public void SelectServiceCategoryFilter(string category)
    {
        SelectedServiceCategory = category;
        foreach (var chip in ServiceCategoryChips)
        {
            chip.IsSelected = chip.Name.Equals(category, StringComparison.OrdinalIgnoreCase);
        }
        ApplyServiceFilter();
    }

    public void ApplyServiceFilter()
    {
        var query = _allServices.AsEnumerable();

        if (HideMicrosoftServices)
        {
            query = query.Where(s => s.IsThirdParty);
        }
        else if (SelectedServiceCategory != "All")
        {
            query = SelectedServiceCategory switch
            {
                "ThirdParty" => query.Where(s => s.IsThirdParty),
                "SafeToDisable" => query.Where(s => s.IsSafeToDisable),
                "Recommended" => query.Where(s => s.Category == "Recommended"),
                "MandatoryCore" => query.Where(s => s.IsMandatory),
                _ => query
            };
        }

        if (!string.IsNullOrWhiteSpace(SelectedServiceCompany) && SelectedServiceCompany != "All Companies")
        {
            query = query.Where(s => s.Company.Equals(SelectedServiceCompany, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(ServiceSearchQuery))
        {
            var q = ServiceSearchQuery.Trim();
            query = query.Where(s =>
                s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Company.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.OrderBy(s => s.DisplayName).ToList();
        ServicesList.Clear();
        foreach (var s in filtered)
        {
            ServicesList.Add(s);
        }
    }

    private void UpdateServiceChips()
    {
        TotalServicesCount = _allServices.Count;
        ThirdPartyServicesCount = _allServices.Count(s => s.IsThirdParty);
        SafeToDisableServicesCount = _allServices.Count(s => s.IsSafeToDisable);
        MandatoryServicesCount = _allServices.Count(s => s.IsMandatory);
        RunningServicesCount = _allServices.Count(s => s.IsRunning);
        var recommendedCount = _allServices.Count(s => s.Category == "Recommended");

        ServiceCategoryChips.Clear();
        ServiceCategoryChips.Add(new CategoryFilterItem
        {
            Name = "All",
            DisplayName = "All Services",
            Icon = "🌟",
            Count = TotalServicesCount,
            IsSelected = SelectedServiceCategory == "All"
        });
        ServiceCategoryChips.Add(new CategoryFilterItem
        {
            Name = "ThirdParty",
            DisplayName = "Third-Party & OEM",
            Icon = "🏢",
            Count = ThirdPartyServicesCount,
            IsSelected = SelectedServiceCategory == "ThirdParty"
        });
        ServiceCategoryChips.Add(new CategoryFilterItem
        {
            Name = "SafeToDisable",
            DisplayName = "Safe to Turn Off",
            Icon = "⚡",
            Count = SafeToDisableServicesCount,
            IsSelected = SelectedServiceCategory == "SafeToDisable"
        });
        ServiceCategoryChips.Add(new CategoryFilterItem
        {
            Name = "Recommended",
            DisplayName = "Recommended",
            Icon = "⚙️",
            Count = recommendedCount,
            IsSelected = SelectedServiceCategory == "Recommended"
        });
        ServiceCategoryChips.Add(new CategoryFilterItem
        {
            Name = "MandatoryCore",
            DisplayName = "Mandatory Core",
            Icon = "🛡️",
            Count = MandatoryServicesCount,
            IsSelected = SelectedServiceCategory == "MandatoryCore"
        });
    }

    [RelayCommand]
    private async Task TurnOffAllSafeServices()
    {
        var targets = _allServices.Where(s => s.IsSafeToDisable && s.IsRunning).ToList();
        if (targets.Count == 0)
        {
            AddLog("⚡ All safe-to-disable services are already stopped.");
            return;
        }

        AddLog($"Stopping {targets.Count} safe-to-disable services (telemetry, feedback, tracking)...");
        int stoppedCount = 0;
        foreach (var svc in targets)
        {
            var ok = await _systemToolsService.StartOrStopServiceAsync(svc, false);
            if (ok) stoppedCount++;
        }
        UpdateServiceChips();
        ApplyServiceFilter();
        AddLog($"✅ Successfully turned off {stoppedCount} of {targets.Count} safe-to-disable background services.");
    }

    [RelayCommand]
    private async Task ToggleServiceState(ServiceItem item)
    {
        if (item.IsMandatory)
        {
            AddLog($"🛡️ Action Blocked: '{item.DisplayName}' is a mandatory Windows system core service. Stopping it is blocked to prevent system instability.");
            return;
        }

        var targetStart = !item.IsRunning;
        var action = targetStart ? "Starting" : "Stopping";
        AddLog($"{action} service '{item.DisplayName}' ({item.Company})...");

        var ok = await _systemToolsService.StartOrStopServiceAsync(item, targetStart);
        if (ok)
        {
            AddLog($"✅ Service '{item.DisplayName}' is now {item.Status}.");
            UpdateServiceChips();
        }
        else
        {
            AddLog($"❌ Failed to change state of '{item.DisplayName}'.");
        }
    }

    [RelayCommand]
    private async Task SetServiceStartup(object[] args)
    {
        if (args?.Length == 2 && args[0] is ServiceItem item && args[1] is string startupType)
        {
            if (item.IsMandatory && startupType.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                AddLog($"🛡️ Action Blocked: Cannot disable mandatory Windows system service '{item.DisplayName}'.");
                return;
            }

            AddLog($"Setting '{item.DisplayName}' startup type to {startupType}...");
            var ok = await _systemToolsService.SetServiceStartupTypeAsync(item, startupType);
            if (ok)
            {
                AddLog($"✅ '{item.DisplayName}' set to {startupType}.");
            }
        }
    }

    // ========================================================
    // Disk Space Analyzer / SpaceSniffer Treemap Commands
    // ========================================================
    [RelayCommand]
    public async Task AnalyzeDiskSpace()
    {
        CurrentDiskPath = SelectedDrive;
        await ScanPathAsync(SelectedDrive);
    }

    [RelayCommand]
    public async Task DrillDown(DiskFolderItem item)
    {
        if (item == null || !item.IsDirectory) return;
        CurrentDiskPath = item.Path;
        await ScanPathAsync(item.Path);
    }

    [RelayCommand]
    public async Task NavigateUp()
    {
        if (string.IsNullOrWhiteSpace(CurrentDiskPath)) return;
        var parent = Directory.GetParent(CurrentDiskPath);
        if (parent != null)
        {
            CurrentDiskPath = parent.FullName;
            await ScanPathAsync(parent.FullName);
        }
        else
        {
            CurrentDiskPath = SelectedDrive;
            await ScanPathAsync(SelectedDrive);
        }
    }

    [RelayCommand]
    private void ToggleView()
    {
        IsTreemapView = !IsTreemapView;
    }

    [RelayCommand]
    public async Task LaunchSpaceSniffer()
    {
        AddLog("Checking for SpaceSniffer utility on system...");
        StatusMessage = "Launching SpaceSniffer...";

        // Check if SpaceSniffer is available in PATH or common paths
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SpaceSniffer", "SpaceSniffer.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SpaceSniffer", "SpaceSniffer.exe"),
            @"C:\ProgramData\chocolatey\bin\SpaceSniffer.exe"
        };

        string? foundPath = commonPaths.FirstOrDefault(File.Exists);

        if (foundPath != null)
        {
            AddLog($"Found SpaceSniffer at: {foundPath}. Launching...");
            Process.Start(new ProcessStartInfo { FileName = foundPath, Arguments = SelectedDrive, UseShellExecute = true });
            StatusMessage = "SpaceSniffer launched.";
            return;
        }

        // Prompt user and offer to install via winget
        AddLog("SpaceSniffer is not installed. Downloading & installing portable SpaceSniffer via winget...");
        StatusMessage = "Installing SpaceSniffer via winget...";

        try
        {
            var res = await _systemToolsService.ExecutePowerShellAsync("winget install --id UderzoSoftware.SpaceSniffer -e --silent --accept-source-agreements --accept-package-agreements", AddLog);
            if (res.Success)
            {
                AddLog("SpaceSniffer installation finished. Attempting launch...");
                StatusMessage = "SpaceSniffer installed successfully. Launching...";
                Process.Start(new ProcessStartInfo { FileName = "SpaceSniffer.exe", Arguments = SelectedDrive, UseShellExecute = true });
            }
            else
            {
                StatusMessage = "Could not automatically download SpaceSniffer. Opening official website...";
                Process.Start(new ProcessStartInfo { FileName = "https://www.uderzo.it/main_products/space_sniffer/", UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AddLog($"SpaceSniffer launch error: {ex.Message}");
            Process.Start(new ProcessStartInfo { FileName = "https://www.uderzo.it/main_products/space_sniffer/", UseShellExecute = true });
        }
    }

    private async Task ScanPathAsync(string targetPath)
    {
        if (IsDiskScanning) return;
        IsDiskScanning = true;
        DiskFolders.Clear();
        _activeCts = new CancellationTokenSource();

        CanNavigateUp = Directory.GetParent(targetPath) != null;
        AddLog($"Analyzing storage on '{targetPath}'...");
        StatusMessage = $"Analyzing treemap visualizer on '{targetPath}'...";

        try
        {
            var items = await _systemToolsService.AnalyzeDiskSpaceAsync(targetPath, _activeCts.Token);
            foreach (var item in items)
            {
                DiskFolders.Add(item);
            }
            StatusMessage = $"Analysis complete: {DiskFolders.Count} largest items identified in '{targetPath}'.";
            AddLog($"✅ Analysis completed: {DiskFolders.Count} items visualized.");
        }
        catch (Exception ex)
        {
            AddLog($"Disk scan error: {ex.Message}");
        }
        finally
        {
            IsDiskScanning = false;
            _activeCts = null;
        }
    }

    [RelayCommand]
    private void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }
        catch { }
    }

    // ========================================================
    // Windows Optional Features Commands
    // ========================================================
    [RelayCommand]
    public async Task LoadOptionalFeatures()
    {
        OptionalFeatures.Clear();
        AddLog("Scanning Windows Optional Features...");

        try
        {
            var features = await _systemToolsService.GetOptionalFeaturesAsync();
            foreach (var f in features)
            {
                OptionalFeatures.Add(f);
            }
            AddLog($"Loaded {OptionalFeatures.Count} optional features.");
        }
        catch (Exception ex)
        {
            AddLog($"Error querying optional features: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleOptionalFeature(OptionalFeatureItem item)
    {
        AddLog($"Toggling feature '{item.DisplayName}'...");
        await _systemToolsService.ToggleOptionalFeatureAsync(item, AddLog);
    }

    // ========================================================
    // System Health & Maintenance Commands
    // ========================================================
    [RelayCommand]
    private async Task RunSfc()
    {
        if (IsBusy) return;
        IsBusy = true;
        _activeCts = new CancellationTokenSource();
        StatusMessage = "Running System File Checker (sfc /scannow)...";

        var task = new ActivityTaskItem
        {
            Id = "sfc",
            Name = "System File Checker (SFC)",
            Category = "System Repair",
            Status = ActivityTaskStatus.Running,
            StatusMessage = "Verifying system integrity...",
            Progress = 50
        };
        ActivityService.Instance.StartSession("System Integrity Check", new[] { task });

        try
        {
            await _systemToolsService.RunSfcScanAsync(AddLog, _activeCts.Token);
            StatusMessage = "SFC scan finished. Check log for details.";
            ActivityService.Instance.UpdateTask("sfc", ActivityTaskStatus.Completed, "Integrity check complete", 100);
            ActivityService.Instance.CompleteSession(StatusMessage);
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

        var task = new ActivityTaskItem
        {
            Id = "dism",
            Name = "DISM Component Store Repair",
            Category = "System Repair",
            Status = ActivityTaskStatus.Running,
            StatusMessage = "Restoring component store health...",
            Progress = 50
        };
        ActivityService.Instance.StartSession("DISM Health Restore", new[] { task });

        try
        {
            await _systemToolsService.RunDismRestoreHealthAsync(AddLog, _activeCts.Token);
            StatusMessage = "DISM RestoreHealth completed.";
            ActivityService.Instance.UpdateTask("dism", ActivityTaskStatus.Completed, "Health restored", 100);
            ActivityService.Instance.CompleteSession(StatusMessage);
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

        var task = new ActivityTaskItem
        {
            Id = "dism_cleanup",
            Name = "WinSxS Component Store Deep Cleanup",
            Category = "System Repair",
            Status = ActivityTaskStatus.Running,
            StatusMessage = "Cleaning up superseded components...",
            Progress = 50
        };
        ActivityService.Instance.StartSession("WinSxS Component Cleanup", new[] { task });

        try
        {
            await _systemToolsService.RunDismComponentCleanupAsync(AddLog, _activeCts.Token);
            StatusMessage = "WinSxS Component Store deep cleanup completed.";
            ActivityService.Instance.UpdateTask("dism_cleanup", ActivityTaskStatus.Completed, "Component cleanup finished", 100);
            ActivityService.Instance.CompleteSession(StatusMessage);
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
        ActivityService.Instance.AddLog(msg);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Insert(0, line);
            if (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
