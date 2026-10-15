using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinToolkit.Models;

public partial class AppCategoryGroup : ObservableObject
{
    public required string CategoryName { get; set; }
    public string Icon { get; set; } = "📦";
    public string Description { get; set; } = string.Empty;
    public ObservableCollection<AppItem> Apps { get; set; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private int _totalCount;

    public string ChevronText => IsExpanded ? "▼" : "▶";
    public string ExpandCollapseButtonText => IsExpanded ? "Collapse" : "Expand";
    public string SelectionBadgeText => $"{SelectedCount} of {TotalCount} selected";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ChevronText));
        OnPropertyChanged(nameof(ExpandCollapseButtonText));
    }

    partial void OnSelectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionBadgeText));
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(SelectionBadgeText));
    }

    public void AddApp(AppItem app)
    {
        app.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppItem.IsSelected))
            {
                UpdateCounts();
            }
        };
        Apps.Add(app);
        UpdateCounts();
    }

    public void UpdateCounts()
    {
        SelectedCount = Apps.Count(a => a.IsSelected);
        TotalCount = Apps.Count;
    }

    [RelayCommand]
    public void SelectSection()
    {
        foreach (var app in Apps)
        {
            app.IsSelected = true;
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void ClearSection()
    {
        foreach (var app in Apps)
        {
            app.IsSelected = false;
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
