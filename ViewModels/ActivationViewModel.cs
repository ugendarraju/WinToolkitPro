using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinToolkit.ViewModels;

public partial class ActivationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedActivation = "Both"; // "Windows", "Office", "Both"

    [ObservableProperty]
    private string _statusMessage = "Select the target product and click '⚡ Run Activation' to launch the automated activation utility.";

    [ObservableProperty]
    private bool _isWindowsSelected;

    [ObservableProperty]
    private bool _isOfficeSelected;

    [ObservableProperty]
    private bool _isBothSelected = true;

    [RelayCommand]
    public void SetSelection(string option)
    {
        SelectedActivation = option;
        IsWindowsSelected = option == "Windows";
        IsOfficeSelected = option == "Office";
        IsBothSelected = option == "Both";
    }

    [RelayCommand]
    public void RunActivation(string? customSelection = null)
    {
        string target = customSelection ?? SelectedActivation;
        string switches = "";

        // Map UI selection to the correct switch
        switch (target)
        {
            case "Windows": switches = "/HWID"; break;
            case "Office":  switches = "/Ohook"; break;
            case "Both":    switches = "/HWID /Ohook"; break;
            default:        switches = "/HWID /Ohook"; break;
        }

        // Build the PowerShell command
        string command = $"& ([ScriptBlock]::Create((irm https://get.activated.win))) {switches}";

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            // Use -ExecutionPolicy Bypass to allow the script to run
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            Verb = "runas", // Request Admin privileges
            UseShellExecute = true
        };

        try
        {
            StatusMessage = $"Launching elevated PowerShell activation ({target} - switches: {switches})...";
            Process.Start(psi);
            StatusMessage = $"Activation process started for {target}. An elevated PowerShell window has opened. Follow its on-screen prompt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to launch activation: {ex.Message}";
        }
    }
}
