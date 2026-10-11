using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class UwpAppItem : ObservableObject
{
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public required string PackageFullName { get; set; }
    public string Publisher { get; set; } = string.Empty;
    public bool IsBloatware { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _status = "Installed";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isRemoved;
}
