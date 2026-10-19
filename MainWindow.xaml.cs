using System.Windows;
using WinToolkit.Services;
using WinToolkit.ViewModels;
using Wpf.Ui.Controls;

namespace WinToolkit;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Initialize Services
        var powerShellService = new PowerShellService();
        var wingetService = new WingetService();
        var catalogService = new CatalogService();
        var tweakService = new TweakService(powerShellService);
        var hardwareService = new HardwareService(powerShellService);
        var driverService = new DriverService(powerShellService);
        var updateService = new WindowsUpdateService(powerShellService);
        var cleanerService = new SystemCleanerService(powerShellService);
        var systemToolsService = new SystemToolsService(powerShellService);
        var uninstallerService = new AdvancedUninstallerService(powerShellService);
        var gitHubUpdateService = new GitHubUpdateService();

        // Initialize ViewModels
        var appsVM = new AppsViewModel(wingetService, catalogService);
        var appUpdaterVM = new AppUpdaterViewModel(wingetService);
        var optimizerVM = new OptimizerViewModel(tweakService, catalogService);
        var driversVM = new DriversViewModel(driverService, hardwareService);
        var updateVM = new WindowsUpdateViewModel(updateService, powerShellService);
        var cleanerVM = new CleanerViewModel(cleanerService);
        var systemToolsVM = new SystemToolsViewModel(systemToolsService);
        var uninstallerVM = new AdvancedUninstallerViewModel(uninstallerService, systemToolsService);
        var activationVM = new ActivationViewModel();

        _viewModel = new MainViewModel(appsVM, appUpdaterVM, optimizerVM, driversVM, updateVM, cleanerVM, systemToolsVM, uninstallerVM, activationVM, gitHubUpdateService);
        DataContext = _viewModel;

        // Instantly start checking for updates in parallel as window is created
        _ = _viewModel.CheckForAppUpdatesAsync();

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}