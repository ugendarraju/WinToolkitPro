using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class DiskFolderItem : ObservableObject
{
    public required string Path { get; set; }
    public required string Name { get; set; }
    public long SizeBytes { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public bool IsDirectory { get; set; } = true;
    public int FileCount { get; set; }

    public string Icon => IsDirectory ? "📁" : "📄";
}
