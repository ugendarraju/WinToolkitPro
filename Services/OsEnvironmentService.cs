using System.Runtime.InteropServices;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class OsEnvironmentService
{
    private static OsEnvironmentService? _instance;
    public static OsEnvironmentService Instance => _instance ??= new OsEnvironmentService();

    public bool Is64BitOperatingSystem => Environment.Is64BitOperatingSystem;
    public bool Is64BitProcess => Environment.Is64BitProcess;
    public string Architecture => Is64BitOperatingSystem ? "x64" : "x86";

    public Version OsVersion => Environment.OSVersion.Version;

    /// <summary>
    /// Windows 11 is NT 10.0 with Build >= 22000
    /// </summary>
    public bool IsWindows11 => OsVersion.Major >= 10 && OsVersion.Build >= 22000;

    /// <summary>
    /// Windows 10 is NT 10.0 with Build < 22000
    /// </summary>
    public bool IsWindows10 => OsVersion.Major == 10 && OsVersion.Build < 22000;

    /// <summary>
    /// Windows 8 or 8.1 is NT 6.2 or 6.3
    /// </summary>
    public bool IsWindows8Or81 => OsVersion.Major == 6 && (OsVersion.Minor == 2 || OsVersion.Minor == 3);

    /// <summary>
    /// Windows 7 is NT 6.1
    /// </summary>
    public bool IsWindows7 => OsVersion.Major == 6 && OsVersion.Minor == 1;

    /// <summary>
    /// Winget (App Installer) is officially supported on Windows 10 1809 (Build 17763)+ and Windows 11.
    /// </summary>
    public bool IsWingetSupported => IsWindows11 || (IsWindows10 && OsVersion.Build >= 17763);

    /// <summary>
    /// Detects if the app is currently running inside Windows Sandbox (temporary disposable VM).
    /// </summary>
    public bool IsWindowsSandbox =>
        string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase) ||
        Environment.MachineName.StartsWith("WDAG", StringComparison.OrdinalIgnoreCase);

    public string OsDisplayName
    {
        get
        {
            var arch = Is64BitOperatingSystem ? "64-bit (x64)" : "32-bit (x86)";
            if (IsWindows11) return $"Windows 11 • {arch}";
            if (IsWindows10) return $"Windows 10 • {arch}";
            if (IsWindows8Or81) return $"Windows 8.1 • {arch}";
            if (IsWindows7) return $"Windows 7 • {arch}";
            return $"Windows (Build {OsVersion.Build}) • {arch}";
        }
    }

    public string ArchitectureBadge => Is64BitOperatingSystem ? "64-BIT (x64)" : "32-BIT (x86)";

    private static readonly HashSet<string> KnownX64OnlyAppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "docker",
        "blender",
        "obs-studio",
        "davinci-resolve",
        "visualstudio2022",
        "unity-hub",
        "epic-games",
        "powershell7-x64"
    };

    /// <summary>
    /// Returns true if the application can be installed on the current OS architecture and Windows version.
    /// </summary>
    public bool IsAppSupported(AppItem app)
    {
        // If system is 32-bit, filter out packages that strictly require 64-bit
        if (!Is64BitOperatingSystem)
        {
            if (app.SupportedArchitectures.Equals("x64", StringComparison.OrdinalIgnoreCase))
                return false;

            if (KnownX64OnlyAppIds.Contains(app.Id))
                return false;
        }

        // Check Windows Version requirement
        if (app.MinWindowsVersion.Equals("11", StringComparison.OrdinalIgnoreCase) && !IsWindows11)
            return false;

        if (app.MinWindowsVersion.Equals("10", StringComparison.OrdinalIgnoreCase) && !IsWindows10 && !IsWindows11)
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if an OS optimization tweak applies to the current OS version and architecture.
    /// </summary>
    public bool IsTweakSupported(TweakItem tweak)
    {
        // Windows 11 exclusive tweaks
        if (tweak.SupportedOs.Equals("win11", StringComparison.OrdinalIgnoreCase) && !IsWindows11)
            return false;

        // Windows 10+ tweaks
        if (tweak.SupportedOs.Equals("win10+", StringComparison.OrdinalIgnoreCase) && !IsWindows10 && !IsWindows11)
            return false;

        // Architecture check
        if (!Is64BitOperatingSystem && tweak.SupportedArch.Equals("x64", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
