using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinToolkit.Models;

public partial class TweakSectionGroup : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "⚙️";
    public string Description { get; set; } = string.Empty;
    public ObservableCollection<TweakItem> Tweaks { get; } = new();

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

    public void AddTweak(TweakItem tweak)
    {
        tweak.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TweakItem.IsChecked))
            {
                UpdateCounts();
            }
        };
        Tweaks.Add(tweak);
        UpdateCounts();
    }

    public void UpdateCounts()
    {
        SelectedCount = Tweaks.Count(t => t.IsChecked);
        TotalCount = Tweaks.Count;
    }

    [RelayCommand]
    public void SelectSection()
    {
        foreach (var tweak in Tweaks)
        {
            tweak.IsChecked = true;
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void ClearSection()
    {
        foreach (var tweak in Tweaks)
        {
            tweak.IsChecked = false;
        }
        UpdateCounts();
    }

    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
