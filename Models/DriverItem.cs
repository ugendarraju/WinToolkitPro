using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class DriverItem : ObservableObject
{
    public required string DeviceName { get; set; }
    public string DeviceClass { get; set; } = "System Device";
    public string Manufacturer { get; set; } = "Unknown";
    public string HardwareId { get; set; } = string.Empty;
    public string CurrentDriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;

    [ObservableProperty]
    private string _status = "Healthy";

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isBusy;
}
