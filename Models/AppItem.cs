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
    private string _installerType = "winget";
    public string InstallerType
    {
        get => string.IsNullOrEmpty(_installerType) ? "winget" : _installerType;
        set => _installerType = string.IsNullOrEmpty(value) ? "winget" : value;
    }

    private string _source = "winget";
    public string Source
    {
        get => string.IsNullOrEmpty(_source) ? "winget" : _source;
        set => _source = string.IsNullOrEmpty(value) ? "winget" : value;
    }

    private string _wingetId = string.Empty;
    public required string WingetId
    {
        get => _wingetId ?? string.Empty;
        set => _wingetId = value ?? string.Empty;
    }
    public required string Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "AppGeneric24";

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

    /// <summary>
    /// Supported architectures: "all" (default), "x64", or "x86"
    /// </summary>
    public string SupportedArchitectures { get; set; } = "all";

    /// <summary>
    /// Minimum Windows version: "7", "8", "10" (default), "11"
    /// </summary>
    public string MinWindowsVersion { get; set; } = "7";

    /// <summary>
    /// Dedicated 32-bit Winget package ID if different from WingetId
    /// </summary>
    public string? X86WingetId { get; set; }

    /// <summary>
    /// Dedicated 32-bit direct download URL if different from DownloadUrl
    /// </summary>
    public string? X86DownloadUrl { get; set; }

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
