using System;
using System.Collections.Generic;
using WinToolkit.Models;

namespace WinToolkit.Services;

public static class ServiceClassifier
{
    private static readonly HashSet<string> MandatoryServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "PlugPlay", "EventLog", "CryptSvc",
        "SamSs", "LSM", "Winmgmt", "Schedule", "Dhcp", "Dnscache", "mpssvc",
        "ProfSvc", "CoreMessagingRegistrar", "Power", "SystemEventsBroker",
        "TimeBrokerSvc", "UserManager", "BrokerInfrastructure", "sppsvc",
        "LanmanWorkstation", "nsi", "netprofm", "StateRepository", "AppXSvc",
        "Wcmsvc", "FontCache", "KeyIso", "VaultSvc", "EventSystem", "BFE",
        "gpsvc", "SecurityHealthService", "TokenBroker", "SENS", "WpnService"
    };

    private static readonly HashSet<string> SafeToDisableServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiagTrack", "dmwappushservice", "WerSvc", "RemoteRegistry", "RetailDemo",
        "MapsBroker", "SensorService", "SensrSvc", "wisvc", "WpcMonSvc",
        "SharedRealitySvc", "TroubleshootingSvc", "WalletService", "Fax",
        "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc",
        "MixedRealityOpenXRSvc", "SEMgrSvc", "PhoneSvc", "AJRouter", "fhsvc",
        "tzautoupdate", "irmon", "shpamsvc", "SysMain", "WbioSrvc"
    };

    public static void Classify(ServiceItem item)
    {
        var path = item.PathName ?? string.Empty;
        var name = item.Name ?? string.Empty;
        var disp = item.DisplayName ?? string.Empty;
        var combined = $"{name} {disp} {path}";

        // 1. Detect Company / Software Vendor
        item.Company = DetectCompany(name, disp, path, combined, out bool isThirdParty);
        item.IsThirdParty = isThirdParty;

        // Apply vendor specific badge styling
        ApplyVendorBadgeStyling(item);

        // 2. Classify Category and Safety Recommendations
        if (item.IsThirdParty)
        {
            item.Category = "ThirdParty";
            item.CategoryDisplayName = "🏢 Third-Party";
            item.BadgeBackground = "#1e1b4b";
            item.BadgeBorder = "#818cf8";
            item.BadgeForeground = "#c7d2fe";
            item.IsMandatory = false;
            item.IsSafeToDisable = false;
        }
        else if (MandatoryServices.Contains(name) ||
                 disp.StartsWith("Remote Procedure Call", StringComparison.OrdinalIgnoreCase) ||
                 disp.StartsWith("Power", StringComparison.OrdinalIgnoreCase) ||
                 disp.StartsWith("Plug and Play", StringComparison.OrdinalIgnoreCase) ||
                 disp.StartsWith("Windows Event Log", StringComparison.OrdinalIgnoreCase) ||
                 disp.StartsWith("Local Session Manager", StringComparison.OrdinalIgnoreCase) ||
                 disp.StartsWith("Security Accounts Manager", StringComparison.OrdinalIgnoreCase))
        {
            item.Category = "MandatoryCore";
            item.CategoryDisplayName = "🛡️ Mandatory Core";
            item.IsMandatory = true;
            item.IsSafeToDisable = false;
            item.BadgeBackground = "#450a0a";
            item.BadgeBorder = "#ef4444";
            item.BadgeForeground = "#fca5a5";
        }
        else if (SafeToDisableServices.Contains(name) ||
                 disp.Contains("Telemetry", StringComparison.OrdinalIgnoreCase) ||
                 disp.Contains("Diagnostic Tracking", StringComparison.OrdinalIgnoreCase) ||
                 disp.Contains("Customer Experience", StringComparison.OrdinalIgnoreCase) ||
                 disp.Contains("Error Reporting", StringComparison.OrdinalIgnoreCase) ||
                 disp.Contains("Retail Demo", StringComparison.OrdinalIgnoreCase) ||
                 disp.Contains("Downloaded Maps", StringComparison.OrdinalIgnoreCase))
        {
            item.Category = "SafeToDisable";
            item.CategoryDisplayName = "⚡ Safe to Turn Off";
            item.IsSafeToDisable = true;
            item.IsMandatory = false;
            item.BadgeBackground = "#064e3b";
            item.BadgeBorder = "#10b981";
            item.BadgeForeground = "#6ee7b7";
        }
        else
        {
            item.Category = "Recommended";
            item.CategoryDisplayName = "⚙️ Microsoft Recommended";
            item.IsMandatory = false;
            item.IsSafeToDisable = false;
            item.BadgeBackground = "#0c4a6e";
            item.BadgeBorder = "#0284c7";
            item.BadgeForeground = "#7dd3fc";
        }
    }

    private static string DetectCompany(string name, string disp, string path, string combined, out bool isThirdParty)
    {
        if (combined.Contains("ASUS", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Armoury", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("AsusPTP", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("ROG", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "ASUS";
        }
        if (combined.Contains("Adobe", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Acrobat", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("AdobeARM", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Adobe";
        }
        if (combined.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("gupdate", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Google";
        }
        if (combined.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("NvContainer", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("NVDisplay", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "NVIDIA";
        }
        if (combined.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            !combined.Contains("Intelligent", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Intel";
        }
        if (combined.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "AMD";
        }
        if (combined.Contains("Razer", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Chroma", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Synapse", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Razer";
        }
        if (combined.Contains("Corsair", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("iCUE", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Corsair";
        }
        if (combined.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("LGHUB", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Logitech";
        }
        if (combined.Contains("Dell", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Alienware", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Dell";
        }
        if (combined.Contains("HP ", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("HPSupport", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "HP";
        }
        if (combined.Contains("Lenovo", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Vantage", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Lenovo";
        }
        if (combined.Contains("Acer", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Acer";
        }
        if (combined.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Valve", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Valve / Steam";
        }
        if (combined.Contains("Epic", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("EasyAntiCheat", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("BattlEye", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Epic / Anti-Cheat";
        }
        if (combined.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Bonjour", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Apple";
        }
        if (combined.Contains("Brave", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Brave";
        }
        if (combined.Contains("Mozilla", StringComparison.OrdinalIgnoreCase))
        {
            isThirdParty = true;
            return "Mozilla";
        }

        // Check if executable is installed in Program Files or ProgramData outside Windows OS
        if (!string.IsNullOrWhiteSpace(path) &&
            !path.Contains(@"\Windows\System32\svchost.exe", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains(@"\Windows\system32\", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) &&
            (path.Contains(@"Program Files", StringComparison.OrdinalIgnoreCase) ||
             path.Contains(@"AppData", StringComparison.OrdinalIgnoreCase) ||
             path.Contains(@"ProgramData", StringComparison.OrdinalIgnoreCase)))
        {
            isThirdParty = true;
            return ExtractVendorFromPath(path);
        }

        isThirdParty = false;
        return "Microsoft Corporation";
    }

    private static string ExtractVendorFromPath(string path)
    {
        try
        {
            var cleaned = path.Trim('\"').Trim();
            var parts = cleaned.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("Program Files", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                    parts[i].Equals("ProgramData", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < parts.Length)
                    {
                        var folder = parts[i + 1];
                        if (!folder.Equals("Common Files", StringComparison.OrdinalIgnoreCase) &&
                            !folder.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase))
                        {
                            return folder;
                        }
                        if (folder.Equals("Common Files", StringComparison.OrdinalIgnoreCase) && i + 2 < parts.Length)
                        {
                            return parts[i + 2];
                        }
                    }
                }
            }
        }
        catch { }
        return "Third-Party Vendor";
    }

    private static void ApplyVendorBadgeStyling(ServiceItem item)
    {
        switch (item.Company.ToUpperInvariant())
        {
            case "ASUS":
                item.CompanyBadgeBackground = "#3b0764";
                item.CompanyBadgeForeground = "#c084fc";
                break;
            case "ADOBE":
                item.CompanyBadgeBackground = "#450a0a";
                item.CompanyBadgeForeground = "#f87171";
                break;
            case "GOOGLE":
                item.CompanyBadgeBackground = "#431407";
                item.CompanyBadgeForeground = "#fb923c";
                break;
            case "NVIDIA":
                item.CompanyBadgeBackground = "#14532d";
                item.CompanyBadgeForeground = "#4ade80";
                break;
            case "INTEL":
                item.CompanyBadgeBackground = "#082f49";
                item.CompanyBadgeForeground = "#38bdf8";
                break;
            case "AMD":
                item.CompanyBadgeBackground = "#450a0a";
                item.CompanyBadgeForeground = "#f87171";
                break;
            case "RAZER":
                item.CompanyBadgeBackground = "#052e16";
                item.CompanyBadgeForeground = "#22c55e";
                break;
            case "CORSAIR":
                item.CompanyBadgeBackground = "#1e293b";
                item.CompanyBadgeForeground = "#facc15";
                break;
            case "LOGITECH":
                item.CompanyBadgeBackground = "#0e7490";
                item.CompanyBadgeForeground = "#67e8f9";
                break;
            case "MICROSOFT CORPORATION":
                item.CompanyBadgeBackground = "#1e293b";
                item.CompanyBadgeForeground = "#94a3b8";
                break;
            default:
                item.CompanyBadgeBackground = "#1e293b";
                item.CompanyBadgeForeground = item.IsThirdParty ? "#38bdf8" : "#94a3b8";
                break;
        }
    }
}
