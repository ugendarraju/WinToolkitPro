using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class ServiceItem : ObservableObject
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private string _status = "Stopped";

    [ObservableProperty]
    private string _startupType = "Manual";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    public string ToggleButtonText => IsRunning ? "⏹️ Stop" : "▶️ Start";
}
