using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class AppUpgradeItem : ObservableObject
{
    public required string Name { get; set; }
    public required string PackageId { get; set; }
    public required string InstalledVersion { get; set; }
    public required string AvailableVersion { get; set; }
    public string Source { get; set; } = "winget";

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _statusMessage = "Update Available";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
}
