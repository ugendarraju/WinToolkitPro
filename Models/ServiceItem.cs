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

    public string PathName { get; set; } = string.Empty;
    public string Company { get; set; } = "Microsoft Corporation";
    public string Category { get; set; } = "Recommended";
    public string CategoryDisplayName { get; set; } = "⚙️ Recommended";
    public bool IsMandatory { get; set; } = false;
    public bool IsSafeToDisable { get; set; } = false;
    public bool IsThirdParty { get; set; } = false;

    public string BadgeBackground { get; set; } = "#0c4a6e";
    public string BadgeBorder { get; set; } = "#0284c7";
    public string BadgeForeground { get; set; } = "#7dd3fc";

    public string CompanyBadgeBackground { get; set; } = "#1e293b";
    public string CompanyBadgeForeground { get; set; } = "#94a3b8";

    public string ToggleButtonText => IsRunning ? "⏹️ Stop" : "▶️ Start";
    public bool CanToggle => !IsMandatory;
    public string ProtectionTooltip => IsMandatory 
        ? "Shield Protected: This is a critical Windows system core service. Stopping or disabling it can cause Windows instability or crashes." 
        : (IsRunning ? "Stop this background service" : "Start this background service");
}
