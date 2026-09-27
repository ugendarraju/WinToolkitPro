using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public enum InstallStatus
{
    Idle,
    Queued,
    Downloading,
    Installing,
    Installed,
    Failed
}

public partial class AppItem : ObservableObject
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string WingetId { get; set; }
    public required string Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "AppGeneric24";

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private InstallStatus _status = InstallStatus.Idle;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progress = 0.0;
}
