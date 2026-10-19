using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class BrowserExtensionItem : ObservableObject
{
    public required string Browser { get; set; } // Chrome, Edge, Firefox, Brave
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _status = "Active";

    [ObservableProperty]
    private bool _isBusy;
}
