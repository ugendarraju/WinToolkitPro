using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public enum ActivityTaskStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public partial class ActivityTaskItem : ObservableObject
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [ObservableProperty]
    private ActivityTaskStatus _status = ActivityTaskStatus.Queued;

    [ObservableProperty]
    private string _statusMessage = "Queued";

    [ObservableProperty]
    private double _progress = 0;
}
