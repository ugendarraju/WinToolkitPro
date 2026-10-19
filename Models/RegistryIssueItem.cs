using CommunityToolkit.Mvvm.ComponentModel;

namespace WinToolkit.Models;

public partial class RegistryIssueItem : ObservableObject
{
    public required string Category { get; set; } // e.g. "Missing Shared DLL", "Stale MuiCache", "Invalid App Path"
    public required string KeyPath { get; set; }   // e.g. HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls
    public required string ValueName { get; set; } // The registry value name
    public string Details { get; set; } = string.Empty; // Description of why it is broken

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private string _status = "Issue Detected";
}
