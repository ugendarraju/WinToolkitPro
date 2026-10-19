using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public enum LeftoverType
{
    File,
    Folder,
    RegistryKey,
    RegistryValue
}

public partial class UninstallLeftoverItem : ObservableObject
{
    public required LeftoverType Type { get; set; }
    public required string Path { get; set; }
    public string Details { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public string TypeIcon => Type switch
    {
        LeftoverType.Folder => "📁",
        LeftoverType.File => "📄",
        LeftoverType.RegistryKey => "🗝️",
        LeftoverType.RegistryValue => "⚙️",
        _ => "📝"
    };

    public string SizeFormatted
    {
        get
        {
            if (SizeBytes <= 0) return "--";
            if (SizeBytes >= 1024 * 1024)
                return $"{(SizeBytes / (1024.0 * 1024.0)):F2} MB";
            if (SizeBytes >= 1024)
                return $"{(SizeBytes / 1024.0):F1} KB";
            return $"{SizeBytes} B";
        }
    }

    [ObservableProperty]
    private bool _isSelected = true;
}
