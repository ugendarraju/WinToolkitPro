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

    /// <summary>
    /// Type of installer: "winget" (default), "exe_download", or "zip_bat" (download zip, extract, and execute .bat)
    /// </summary>
    public string InstallerType { get; set; } = "winget";

    /// <summary>
    /// Package source repository: "winget" (default), or "msstore"
    /// </summary>
    public string Source { get; set; } = "winget";

    /// <summary>
    /// Direct download URL if not using standard winget or for custom zip
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Script or batch file name inside extracted zip, e.g. "install_all.bat"
    /// </summary>
    public string? BatFile { get; set; }

    /// <summary>
    /// Arguments for the batch file, e.g. "-ai"
    /// </summary>
    public string? BatArguments { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveStatus))]
    private InstallStatus _status = InstallStatus.Idle;

    public bool HasActiveStatus => Status != InstallStatus.Idle;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progress = 0.0;
}
