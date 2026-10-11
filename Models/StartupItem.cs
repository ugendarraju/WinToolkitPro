using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class StartupItem : ObservableObject
{
    public required string Name { get; set; }
    public required string Command { get; set; }
    public string Location { get; set; } = "HKCU Run";
    public string RegistryKey { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Impact { get; set; } = "Medium";

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private bool _isBusy;
}
