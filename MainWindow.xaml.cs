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
        var driverService = new DriverService(powerShellService);
        var updateService = new WindowsUpdateService(powerShellService);
        var cleanerService = new SystemCleanerService(powerShellService);

        // Initialize ViewModels
        var appsVM = new AppsViewModel(wingetService, catalogService);
        var optimizerVM = new OptimizerViewModel(tweakService, catalogService);
        var driversVM = new DriversViewModel(driverService);
        var updateVM = new WindowsUpdateViewModel(updateService, powerShellService);
        var cleanerVM = new CleanerViewModel(cleanerService);

        _viewModel = new MainViewModel(appsVM, optimizerVM, driversVM, updateVM, cleanerVM);
        DataContext = _viewModel;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}