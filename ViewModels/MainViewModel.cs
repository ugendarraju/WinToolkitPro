using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty]
    private int _selectedNavigationIndex = 0;

    [ObservableProperty]
    private string _systemInfo = string.Empty;

    [ObservableProperty]
    private bool _isAdministrator;

    public bool CanElevate => !IsAdministrator;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    public MainViewModel(
        AppsViewModel appsVM,
        AppUpdaterViewModel appUpdaterVM,
        OptimizerViewModel optimizerVM,
        DriversViewModel driversVM,
        WindowsUpdateViewModel windowsUpdateVM,
        CleanerViewModel cleanerVM,
        SystemToolsViewModel systemToolsVM,
        ActivationViewModel activationVM)
    {
        AppsVM = appsVM;
        AppUpdaterVM = appUpdaterVM;
        OptimizerVM = optimizerVM;
        DriversVM = driversVM;
        WindowsUpdateVM = windowsUpdateVM;
        CleanerVM = cleanerVM;
        SystemToolsVM = systemToolsVM;
        ActivationVM = activationVM;

        CheckPrivilegesAndSystem();
    }

    public async Task InitializeAsync()
    {
        await AppsVM.InitializeAsync();
        await OptimizerVM.InitializeAsync();
        await SystemToolsVM.InitializeAsync();
        await DriversVM.InitializeAsync();
    }

    private void CheckPrivilegesAndSystem()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        IsAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);

        var os = Environment.OSVersion;
        var machine = Environment.MachineName;
        var arch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        SystemInfo = $"{machine} | Windows {os.Version.Major}.{os.Version.Build} ({arch}) | Admin: {(IsAdministrator ? "Yes" : "No")}";
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
