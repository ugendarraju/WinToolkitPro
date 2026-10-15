using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class CategoryFilterItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;
}
