using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;

namespace WinToolkit.ViewModels;

public class AppCategoryGroup
{
    public required string CategoryName { get; set; }
    public ObservableCollection<AppItem> Apps { get; set; } = new();
}

public partial class AppsViewModel : ObservableObject
{
    private readonly WingetService _wingetService;
    private readonly CatalogService _catalogService;
    private CancellationTokenSource? _installCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "All";

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _isWingetAvailable = true;

    [ObservableProperty]
    private double _overallProgress;

    public ObservableCollection<AppItem> AllApps { get; } = new();
    public ObservableCollection<AppItem> FilteredApps { get; } = new();
    public ObservableCollection<AppCategoryGroup> GroupedApps { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public AppsViewModel(WingetService wingetService, CatalogService catalogService)
    {
        _wingetService = wingetService;
        _catalogService = catalogService;
    }

    public async Task InitializeAsync()
    {
        IsWingetAvailable = await _wingetService.IsWingetAvailableAsync();
        if (!IsWingetAvailable)
        {
            StatusText = "Notice: winget is not installed or detected in PATH.";
        }

        var apps = await _catalogService.LoadAppsAsync();
        AllApps.Clear();
        Categories.Clear();
        Categories.Add("All");

        // Fixed category order matching standard installer sections
        var categoryOrder = new List<string>
        {
            "Web Browsers", "Messaging", "Media", ".NET", "Java",
            "Imaging", "3D Printing", "Documents", "Security",
            "Utilities", "Compression", "File Sharing", "Other",
            "Online Storage", "Developer Tools", "VC++ Redistributables"
        };

        var loadedCategories = apps.Select(a => a.Category).Distinct().ToList();
        foreach (var cat in categoryOrder)
        {
            if (loadedCategories.Contains(cat) && !Categories.Contains(cat))
            {
                Categories.Add(cat);
            }
        }
        foreach (var cat in loadedCategories)
        {
            if (!Categories.Contains(cat))
            {
                Categories.Add(cat);
            }
        }

        foreach (var app in apps)
        {
            app.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppItem.IsSelected))
                {
                    UpdateSelectedCount();
                }
            };
            AllApps.Add(app);
        }

        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        GroupedApps.Clear();

        var query = SearchQuery?.Trim() ?? string.Empty;

        var filtered = AllApps.Where(a =>
            (SelectedCategory == "All" || a.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(query) ||
             a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             a.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             a.WingetId.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var item in filtered)
        {
            FilteredApps.Add(item);
        }

        // Group by category while preserving clean section order
        var groups = filtered
            .GroupBy(a => a.Category)
            .OrderBy(g => Categories.IndexOf(g.Key) >= 0 ? Categories.IndexOf(g.Key) : 999);

        foreach (var group in groups)
        {
            var grp = new AppCategoryGroup { CategoryName = group.Key };
            foreach (var app in group)
            {
                grp.Apps.Add(app);
            }
            GroupedApps.Add(grp);
        }
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = AllApps.Count(a => a.IsSelected);
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var app in FilteredApps)
        {
            app.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var app in AllApps)
        {
            app.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task InstallSelected()
    {
        var selectedApps = AllApps.Where(a => a.IsSelected).ToList();
        if (selectedApps.Count == 0) return;

        IsInstalling = true;
        _installCts = new CancellationTokenSource();
        var ct = _installCts.Token;

        var total = selectedApps.Count;
        var current = 0;

        AddLog($"Starting batch silent installation of {total} package(s)...");

        foreach (var app in selectedApps)
        {
            if (ct.IsCancellationRequested) break;

            current++;
            StatusText = $"Installing {app.Name} ({current}/{total})...";
            OverallProgress = ((double)(current - 1) / total) * 100.0;

            await _wingetService.InstallAppAsync(app, AddLog, ct);

            OverallProgress = ((double)current / total) * 100.0;
        }

        IsInstalling = false;
        StatusText = ct.IsCancellationRequested ? "Installation cancelled." : "Batch installation finished!";
        AddLog(StatusText);
    }

    [RelayCommand]
    private void CancelInstallation()
    {
        _installCts?.Cancel();
        StatusText = "Cancelling...";
        AddLog("Cancelling ongoing installations...");
    }

    private void AddLog(string message)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Logs.Insert(0, line);
            if (Logs.Count > 200) Logs.RemoveAt(Logs.Count - 1);
        });
    }
}
