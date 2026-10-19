using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class InstalledSoftwareItem : ObservableObject
{
    public required string DisplayName { get; set; }
    public string DisplayVersion { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string QuietUninstallString { get; set; } = string.Empty;
    public string InstallDate { get; set; } = string.Empty;
    public long EstimatedSizeKb { get; set; }
    public string RegistryKeyPath { get; set; } = string.Empty;
    public string Architecture { get; set; } = "x64"; // x64, x86, User
    public string DisplayIcon { get; set; } = string.Empty;
    public bool IsSystemComponent { get; set; }

    public string SizeFormatted
    {
        get
        {
            if (EstimatedSizeKb <= 0) return "--";
            if (EstimatedSizeKb >= 1024 * 1024)
                return $"{(EstimatedSizeKb / (1024.0 * 1024.0)):F2} GB";
            if (EstimatedSizeKb >= 1024)
                return $"{(EstimatedSizeKb / 1024.0):F1} MB";
            return $"{EstimatedSizeKb} KB";
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _status = "Installed";

    [ObservableProperty]
    private bool _isBusy;
}
