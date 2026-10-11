using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class CleanerTargetItem : ObservableObject
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; } // e.g. "Windows System", "Web Browsers", "Application Caches"
    public required string Description { get; set; }
    public List<string> PathPatterns { get; set; } = new();

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private long _bytesFound;

    [ObservableProperty]
    private int _itemsFound;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private bool _isBusy;

    public string FormattedSize => BytesFound switch
    {
        >= 1024 * 1024 * 1024 => $"{(double)BytesFound / (1024 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{(double)BytesFound / (1024 * 1024):F1} MB",
        >= 1024 => $"{(double)BytesFound / 1024:F0} KB",
        > 0 => $"{BytesFound} B",
        _ => "0 B"
    };
}
