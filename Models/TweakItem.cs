using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class TweakItem : ObservableObject
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = "Safe"; // Safe, Recommended, Advanced
    public string ApplyScript { get; set; } = string.Empty;
    public string RevertScript { get; set; } = string.Empty;
    public string SupportedOs { get; set; } = "all"; // all, win11, win10+, win7+
    public string SupportedArch { get; set; } = "all"; // all, x64, x86

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Ready";
}
