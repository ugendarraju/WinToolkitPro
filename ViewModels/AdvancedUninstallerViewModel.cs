using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;
using WinToolkit.Services;
using WinToolkit.Views;

namespace WinToolkit.ViewModels;

public partial class AdvancedUninstallerViewModel : ObservableObject
{
    private readonly AdvancedUninstallerService _uninstallerService;
    private readonly SystemToolsService _systemToolsService;

    // All Desktop Programs
    public ObservableCollection<InstalledSoftwareItem> AllPrograms { get; } = new();
    public ObservableCollection<InstalledSoftwareItem> FilteredPrograms { get; } = new();

    // Windows UWP Store Apps
    public ObservableCollection<UwpAppItem> AllWindowsApps { get; } = new();
    public ObservableCollection<UwpAppItem> FilteredWindowsApps { get; } = new();

    // Browser Extensions
    public ObservableCollection<BrowserExtensionItem> BrowserExtensions { get; } = new();
    public ObservableCollection<BrowserExtensionItem> FilteredExtensions { get; } = new();

    // Leftover Cleaning Review
    public ObservableCollection<UninstallLeftoverItem> Leftovers { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready to inspect installed software";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _selectedSubTabIndex = 0; // 0=Programs, 1=Windows Apps, 2=Browser Extensions, 3=Leftover Review

    [ObservableProperty]
    private int _scanLevel = 1; // 0=Safe, 1=Moderate, 2=Advanced

    [ObservableProperty]
    private bool _createRestorePoint = true;

    [ObservableProperty]
    private bool _hasLeftovers;

    [ObservableProperty]
    private string _currentAppBeingCleaned = string.Empty;

    [ObservableProperty]
    private string _hunterTargetName = string.Empty;

    [ObservableProperty]
    private string _hunterTargetPath = string.Empty;

    [ObservableProperty]
    private bool _isHunterTargetActive;

    public AdvancedUninstallerViewModel(AdvancedUninstallerService uninstallerService, SystemToolsService systemToolsService)
    {
        _uninstallerService = uninstallerService;
        _systemToolsService = systemToolsService;
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        IsLoading = true;
        StatusText = "Scanning installed applications, Store apps, and extensions...";
        AddLog("Scanning system for desktop software, Windows apps, and browser add-ons...");

        try
        {
            // 1. Scan Desktop software
            var softList = await _uninstallerService.ScanInstalledSoftwareAsync(AddLog);
            
            // 2. Scan UWP Apps
            var uwpList = await _systemToolsService.ScanUwpAppsAsync(AddLog);
            
            // 3. Scan Browser Extensions
            var extList = await _uninstallerService.ScanBrowserExtensionsAsync(AddLog);

            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                AllPrograms.Clear();
                foreach (var item in softList) AllPrograms.Add(item);

                AllWindowsApps.Clear();
                foreach (var item in uwpList) AllWindowsApps.Add(item);

                BrowserExtensions.Clear();
                foreach (var item in extList) BrowserExtensions.Add(item);

                ApplyFilter();
                StatusText = $"Loaded {AllPrograms.Count} desktop apps, {AllWindowsApps.Count} Windows apps, and {BrowserExtensions.Count} browser extensions.";
            });
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
            AddLog($"Scan error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = SearchQuery.Trim().ToLowerInvariant();

        // 1. Desktop Programs
        FilteredPrograms.Clear();
        foreach (var p in AllPrograms)
        {
            if (string.IsNullOrWhiteSpace(q) ||
                p.DisplayName.ToLowerInvariant().Contains(q) ||
                p.Publisher.ToLowerInvariant().Contains(q) ||
                p.InstallLocation.ToLowerInvariant().Contains(q))
            {
                FilteredPrograms.Add(p);
            }
        }

        // 2. Windows Apps
        FilteredWindowsApps.Clear();
        foreach (var u in AllWindowsApps)
        {
            if (string.IsNullOrWhiteSpace(q) ||
                u.DisplayName.ToLowerInvariant().Contains(q) ||
                u.Publisher.ToLowerInvariant().Contains(q) ||
                u.Category.ToLowerInvariant().Contains(q))
            {
                FilteredWindowsApps.Add(u);
            }
        }

        // 3. Browser Extensions
        FilteredExtensions.Clear();
        foreach (var e in BrowserExtensions)
        {
            if (string.IsNullOrWhiteSpace(q) ||
                e.Name.ToLowerInvariant().Contains(q) ||
                e.Browser.ToLowerInvariant().Contains(q) ||
                e.Description.ToLowerInvariant().Contains(q))
            {
                FilteredExtensions.Add(e);
            }
        }
    }

    [RelayCommand]
    private void SelectAllCurrent()
    {
        if (SelectedSubTabIndex == 0)
        {
            foreach (var p in FilteredPrograms) p.IsSelected = true;
        }
        else if (SelectedSubTabIndex == 1)
        {
            foreach (var a in FilteredWindowsApps) a.IsSelected = true;
        }
        else if (SelectedSubTabIndex == 2)
        {
            foreach (var e in FilteredExtensions) e.IsSelected = true;
        }
        else if (SelectedSubTabIndex == 3)
        {
            foreach (var l in Leftovers) l.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearAllCurrent()
    {
        if (SelectedSubTabIndex == 0)
        {
            foreach (var p in FilteredPrograms) p.IsSelected = false;
        }
        else if (SelectedSubTabIndex == 1)
        {
            foreach (var a in FilteredWindowsApps) a.IsSelected = false;
        }
        else if (SelectedSubTabIndex == 2)
        {
            foreach (var e in FilteredExtensions) e.IsSelected = false;
        }
        else if (SelectedSubTabIndex == 3)
        {
            foreach (var l in Leftovers) l.IsSelected = false;
        }
    }

    [RelayCommand]
    public async Task UninstallSingleProgramAsync(InstalledSoftwareItem? item)
    {
        if (item == null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to completely uninstall '{item.DisplayName}' and scan for leftover registry keys and files?",
            "Confirm Full Uninstallation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        item.IsBusy = true;
        item.Status = "Uninstalling...";

        try
        {
            // 1. Create Restore Point if checked
            if (CreateRestorePoint)
            {
                StatusText = $"Creating System Restore Point before removing {item.DisplayName}...";
                await _uninstallerService.CreateSystemRestorePointAsync($"Before removing {item.DisplayName}", AddLog);
            }

            // 2. Execute uninstaller
            StatusText = $"Running official uninstaller for {item.DisplayName}...";
            await _uninstallerService.RunUninstallerAsync(item, silent: false, AddLog);

            // 3. Leftover Scan
            StatusText = $"Scanning for leftover registry traces and files for {item.DisplayName}...";
            var leftovers = await _uninstallerService.ScanLeftoversAsync(item.DisplayName, item.InstallLocation, item.Publisher, ScanLevel, AddLog);

            Leftovers.Clear();
            foreach (var l in leftovers) Leftovers.Add(l);

            CurrentAppBeingCleaned = item.DisplayName;
            HasLeftovers = Leftovers.Count > 0;

            if (HasLeftovers)
            {
                SelectedSubTabIndex = 3; // Switch to Leftovers Review tab
                StatusText = $"Found {Leftovers.Count} leftover item(s). Review and click 'Purge Selected Leftovers'.";
            }
            else
            {
                StatusText = $"✓ {item.DisplayName} successfully uninstalled. No leftovers found.";
                AllPrograms.Remove(item);
                FilteredPrograms.Remove(item);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Uninstall error: {ex.Message}";
            AddLog($"Uninstall error: {ex.Message}");
        }
        finally
        {
            item.IsBusy = false;
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task ForcedUninstallAsync(InstalledSoftwareItem? item)
    {
        string targetName = item?.DisplayName ?? HunterTargetName;
        string targetPath = item?.InstallLocation ?? HunterTargetPath;

        if (string.IsNullOrWhiteSpace(targetName))
        {
            MessageBox.Show("Please enter or select a program name or installation path to perform Forced Uninstall.", "Forced Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Forced Uninstall will forcefully purge registry entries and all files for '{targetName}'.\n\nContinue?",
            "Confirm Forced Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        StatusText = $"Performing Forced Deep Scan for '{targetName}'...";

        try
        {
            if (CreateRestorePoint)
            {
                await _uninstallerService.CreateSystemRestorePointAsync($"Before Forced Uninstall of {targetName}", AddLog);
            }

            var leftovers = await _uninstallerService.ScanLeftoversAsync(targetName, targetPath, "", 2, AddLog);
            Leftovers.Clear();
            foreach (var l in leftovers) Leftovers.Add(l);

            CurrentAppBeingCleaned = targetName;
            HasLeftovers = Leftovers.Count > 0;
            SelectedSubTabIndex = 3;

            StatusText = $"Forced scan found {Leftovers.Count} traces for '{targetName}'. Click 'Purge Selected Leftovers' to eliminate them.";
        }
        catch (Exception ex)
        {
            StatusText = $"Forced uninstall scan failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task BatchUninstallAsync()
    {
        var selected = FilteredPrograms.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Please select at least one program using the checkboxes.", "Batch Uninstall", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"You have selected {selected.Count} program(s) for batch uninstallation.\n\nWinToolkit Pro will attempt silent uninstallation where available and deep-clean remnants. Proceed?",
            "Confirm Batch Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;

        if (CreateRestorePoint)
        {
            StatusText = "Creating System Restore Point before batch uninstallation...";
            await _uninstallerService.CreateSystemRestorePointAsync("Before Batch Uninstallation", AddLog);
        }

        int successCount = 0;
        foreach (var prog in selected)
        {
            StatusText = $"Uninstalling {prog.DisplayName}...";
            prog.IsBusy = true;
            prog.Status = "Uninstalling...";

            await _uninstallerService.RunUninstallerAsync(prog, silent: true, AddLog);

            // Quick leftover scan
            var remnants = await _uninstallerService.ScanLeftoversAsync(prog.DisplayName, prog.InstallLocation, prog.Publisher, 0, AddLog);
            if (remnants.Count > 0)
            {
                await _uninstallerService.PurgeLeftoversAsync(remnants, AddLog);
            }

            prog.Status = "Uninstalled";
            prog.IsBusy = false;
            successCount++;
        }

        StatusText = $"Batch uninstallation complete. Successfully removed {successCount} application(s).";
        AddLog($"[Batch Uninstall] Completed processing {successCount} application(s).");

        // Immediately remove uninstalled items from UI list
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            foreach (var prog in selected)
            {
                AllPrograms.Remove(prog);
                FilteredPrograms.Remove(prog);
            }
        });

        MessageBox.Show(
            $"Batch uninstallation completed successfully!\n\nProcessed: {successCount} application(s).\nResidual leftovers and registry branches were automatically cleaned.",
            "Batch Uninstall Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        await RefreshAllAsync();
    }

    [RelayCommand]
    public async Task PurgeLeftoversAsync()
    {
        var selected = Leftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("No leftovers selected for purging.", "Purge Leftovers", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsLoading = true;
        StatusText = $"Purging {selected.Count} residual file and registry item(s)...";

        try
        {
            var purged = await _uninstallerService.PurgeLeftoversAsync(selected, AddLog);
            StatusText = $"✓ Successfully purged {purged} residual item(s) for {CurrentAppBeingCleaned}.";
            AddLog($"[Uninstaller Pro] Cleaned {purged} leftover trace(s) from system.");

            // Clear leftovers list
            Leftovers.Clear();
            HasLeftovers = false;
            SelectedSubTabIndex = 0; // Return to programs
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Purge error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UninstallUwpAppAsync(UwpAppItem? item)
    {
        if (item == null) return;

        var confirm = MessageBox.Show(
            $"Uninstall Windows App '{item.DisplayName}'?",
            "Confirm App Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        await _systemToolsService.UninstallUwpAppAsync(item, AddLog);
        StatusText = $"Uninstalled {item.DisplayName}.";
        IsLoading = false;
    }

    [RelayCommand]
    public async Task RemoveExtensionAsync(BrowserExtensionItem? item)
    {
        if (item == null) return;

        var confirm = MessageBox.Show(
            $"Remove extension '{item.Name}' from {item.Browser}?",
            "Confirm Extension Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        item.IsBusy = true;
        await _uninstallerService.RemoveBrowserExtensionAsync(item, AddLog);
        BrowserExtensions.Remove(item);
        FilteredExtensions.Remove(item);
        StatusText = $"Removed extension {item.Name}.";
        IsLoading = false;
    }

    [RelayCommand]
    public void LaunchHunterMode()
    {
        var hunter = new HunterModeWindow((procName, procPath) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HunterTargetName = procName;
                HunterTargetPath = procPath;
                IsHunterTargetActive = true;
                StatusText = $"Target locked: {procName} ({procPath})";

                var action = MessageBox.Show(
                    $"🎯 Target Locked:\n\nProcess Name: {procName}\nPath: {procPath}\n\nPerform Forced Uninstall & Leftover Cleanup for this application?",
                    "Target Hunter Pro",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (action == MessageBoxResult.Yes)
                {
                    _ = ForcedUninstallAsync(null);
                }
            });
        });

        hunter.Show();
        StatusText = "🎯 Hunter Mode active: Click and drag the red target onto any window, taskbar icon, or tray app.";
    }

    private void AddLog(string msg)
    {
        ActivityService.Instance.AddLog(msg);
    }
}
