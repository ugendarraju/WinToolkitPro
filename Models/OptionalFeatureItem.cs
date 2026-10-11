using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class OptionalFeatureItem : ObservableObject
{
    public required string FeatureName { get; set; }
    public required string DisplayName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Virtualization & Developer";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(StateBadgeColor))]
    private bool _isEnabled;

    [ObservableProperty]
    private string _state = "Disabled";

    [ObservableProperty]
    private bool _isBusy;

    public string ToggleButtonText => IsEnabled ? "Disable" : "Enable";
    public string StateBadgeColor => IsEnabled ? "#15803d" : "#475569";
}
