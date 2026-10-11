using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class WindowsUpdateItem : ObservableObject
{
    public required string UpdateId { get; set; }
    public required string Title { get; set; }
    public string KBArticle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Quality Update";
    public string Severity { get; set; } = "Important";
    public double SizeMB { get; set; }
    public bool RebootRequired { get; set; }

    public string FormattedSize => SizeMB > 0 ? $"{SizeMB:F1} MB" : "Dynamic / Cumulative";

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private string _errorMessage = string.Empty;
}
