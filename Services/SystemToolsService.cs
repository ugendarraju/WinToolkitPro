using System.Diagnostics;
using System.IO;
using System.Text;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class SystemToolsService
{
    private readonly PowerShellService _powerShellService;

    private record BloatEntry(string NamePattern, string DisplayName, string Category, string Description, bool SafeToRemove = true);

    private static readonly List<BloatEntry> BloatwareCatalog = new()
    {
        // Promotional & Web Wrappers
        new("OutlookForWindows", "Microsoft Outlook (New)", "Promotional Wrapper", "Web wrapper replacing Windows Mail with embedded ads and telemetry", true),
        new("MSTeams", "Microsoft Teams (Personal)", "Background Telemetry", "Preinstalled personal Teams chat client running constantly in background", true),
        new("Client.WebExperience", "Windows Widgets & News Feed", "Telemetry & Ads", "Feeds MSN clickbait news and promotional widgets into Windows taskbar", true),
        new("CrossDevice", "Cross Device Experience Host", "Background Sync", "Background phone linking and cross-device sync telemetry", true),
        new("OfficePushNotificationUtility", "Office Notification Utility", "Promotional Utility", "Pushes Microsoft 365 promotional notifications and subscription prompts", true),
        new("Office.ActionsServer", "Office Actions Server", "Background Service", "Background telemetry service for Microsoft Office integrations", true),
        
        // News, Weather & Assistant
        new("BingNews", "MSN News", "Microsoft Bloatware", "Preinstalled news feed with telemetry and advertisements", true),
        new("BingWeather", "MSN Weather", "Microsoft Bloatware", "Weather app with MSN telemetry and targeted promotions", true),
        new("549981C3F5F10", "Cortana", "Obsolete AI", "Discontinued Cortana voice assistant", true),
        new("Cortana", "Cortana", "Obsolete AI", "Discontinued Cortana voice assistant", true),
        new("Copilot", "Microsoft Copilot", "Microsoft AI", "Copilot web wrapper and sidebar integrations", true),
        new("GetHelp", "Get Help", "Support Redirection", "Diagnostic support and customer assistance redirection", true),
        new("Getstarted", "Tips (Get Started)", "Promotional", "Displays popups and promotional tips for Windows features", true),
        new("FeedbackHub", "Feedback Hub", "Telemetry", "Feedback reporting and diagnostic telemetry collector", true),

        // Entertainment & Media
        new("SolitaireCollection", "Microsoft Solitaire Collection", "Promotional Games", "Preinstalled card games containing full-screen video ads", true),
        new("Clipchamp", "Clipchamp Video Editor", "Cloud Editor", "Freemium cloud video editor with monthly subscription upselling", true),
        new("Spotify", "Spotify Music", "Promotional App", "Preinstalled music streaming client", true),
        new("TikTok", "TikTok", "Promotional App", "Preinstalled social media application", true),
        new("Disney", "Disney+", "Promotional App", "Preinstalled video streaming promotional app", true),
        new("Netflix", "Netflix", "Promotional App", "Preinstalled entertainment app", true),
        new("CandyCrush", "Candy Crush Saga", "OEM Games", "Preinstalled promotional game", true),
        new("LinkedIn", "LinkedIn", "Promotional App", "Preinstalled corporate social network app", true),
        new("Facebook", "Facebook", "Promotional App", "Preinstalled social media app", true),
        new("Instagram", "Instagram", "Promotional App", "Preinstalled photo sharing app", true),

        // Windows Apps rarely needed / alternatives preferred
        new("WindowsMaps", "Windows Maps", "Microsoft Bloatware", "Offline mapping application rarely used on desktop PCs", true),
        new("People", "Microsoft People", "Obsolete Hub", "Deprecated contact hub", true),
        new("Todos", "Microsoft To-Do", "Productivity", "Task management and todo list app", false),
        new("YourPhone", "Phone Link", "Mobile Sync", "Syncs Android and iOS phone notifications and photos", false),
        new("ZuneMusic", "Media Player (Groove)", "Media Apps", "Default Windows audio player", false),
        new("ZuneVideo", "Movies & TV", "Media Store", "Microsoft Movies & TV video player and rental store", true),
        new("WindowsAlarms", "Alarms & Clock", "Windows Utilities", "Clock, timer, and stopwatch app", false),
        new("WindowsCamera", "Camera", "Windows Utilities", "Built-in camera application", false),
        new("WindowsSoundRecorder", "Sound Recorder", "Windows Utilities", "Voice and audio recording tool", false),

        // Gaming Overlays
        new("XboxApp", "Xbox App", "Gaming", "Xbox gaming launcher and PC Game Pass store", true),
        new("GamingApp", "Xbox App", "Gaming", "Xbox gaming launcher and PC Game Pass store", true),
        new("XboxGameOverlay", "Xbox Game Overlay", "Gaming Overlays", "Game capture and DVR background overlay", true),
        new("XboxGamingOverlay", "Xbox Game Bar", "Gaming Overlays", "Xbox Game Bar overlay HUD (Win+G)", true),
        new("XboxIdentityProvider", "Xbox Identity Provider", "Gaming Services", "Xbox authentication system for games", false),
        new("XboxSpeechToTextOverlay", "Xbox Game Speech Overlay", "Gaming Services", "Accessibility speech-to-text overlay for Xbox chat", true),

        // Obsolete / VR / 3D
        new("MixedReality.Portal", "Mixed Reality Portal", "Obsolete VR", "Discontinued Windows Mixed Reality VR environment", true),
        new("3DBuilder", "3D Builder", "Obsolete Apps", "Deprecated 3D model viewer and printing app", true),
        new("MSPaint", "Paint 3D", "Obsolete Apps", "Discontinued 3D paint app (separate from classic Paint)", true),
        new("Paint3D", "Paint 3D", "Obsolete Apps", "Discontinued 3D paint app", true),
        new("SkypeApp", "Skype", "Messaging", "Legacy preinstalled VoIP messaging app", true),
        new("OneNote", "OneNote for Windows 10", "Notes", "Legacy UWP OneNote application", true),
        new("QuickAssist", "Quick Assist", "Remote Assistance", "Remote desktop support assistance tool", false),
    };

    public SystemToolsService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    public Task<(bool Success, string Output, string Error)> ExecutePowerShellAsync(string script, Action<string>? onLog = null)
    {
        return _powerShellService.ExecuteCommandAsync(script, onLog);
    }

    public async Task<List<UwpAppItem>> ScanUwpAppsAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Scanning installed Windows packages and bloatware...");
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var script = @"
            $raw = try { Get-AppxPackage -AllUsers -ErrorAction Stop } catch { Get-AppxPackage -ErrorAction SilentlyContinue }
            $apps = $raw | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | 
                Select-Object Name, PackageFullName, Publisher
            if ($apps) { $apps | ConvertTo-Json -Compress } else { '[]' }
        ";

        var result = await _powerShellService.ExecuteCommandAsync(script, cancellationToken: linkedCts.Token);
        var list = new List<UwpAppItem>();

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            onLog?.Invoke("No removable UWP applications found.");
            return list;
        }

        try
        {
            var raw = result.Output.Trim();
            if (raw.StartsWith("["))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    AddAppFromElement(el, list);
                }
            }
            else if (raw.StartsWith("{"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                AddAppFromElement(doc.RootElement, list);
            }
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Parsing error during package scan: {ex.Message}");
        }

        var bloatCount = list.Count(a => a.IsBloatware);
        onLog?.Invoke($"Found {list.Count} packages ({bloatCount} identified as Windows bloatware).");
        return list;
    }

    private void AddAppFromElement(System.Text.Json.JsonElement el, List<UwpAppItem> list)
    {
        var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
        var pkg = el.TryGetProperty("PackageFullName", out var p) ? p.GetString() ?? "" : "";
        var publisher = el.TryGetProperty("Publisher", out var pub) ? pub.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pkg)) return;

        // Check if matching known bloatware
        var bloatEntry = BloatwareCatalog.FirstOrDefault(b => 
            name.Contains(b.NamePattern, StringComparison.OrdinalIgnoreCase) ||
            b.NamePattern.Contains(name, StringComparison.OrdinalIgnoreCase));

        var isSystemOrUser = IsSystemOrUserApp(name);

        if (bloatEntry != null)
        {
            list.Add(new UwpAppItem
            {
                Name = name,
                DisplayName = bloatEntry.DisplayName,
                PackageFullName = pkg,
                Publisher = publisher,
                Category = bloatEntry.Category,
                Description = bloatEntry.Description,
                IsBloatware = true,
                IsSystemCritical = false,
                IsSelected = bloatEntry.SafeToRemove,
                Status = "Installed (Safe to Debloat)"
            });
        }
        else
        {
            list.Add(new UwpAppItem
            {
                Name = name,
                DisplayName = FormatFriendlyName(name),
                PackageFullName = pkg,
                Publisher = publisher,
                Category = isSystemOrUser ? "System Component / Driver" : "Installed App",
                Description = isSystemOrUser 
                    ? "Essential system driver, codec, or user-installed desktop application." 
                    : "Installed Windows application.",
                IsBloatware = false,
                IsSystemCritical = isSystemOrUser,
                IsSelected = false,
                Status = "Installed"
            });
        }
    }

    private static bool IsSystemOrUserApp(string name)
    {
        // Hardware vendors / Drivers
        if (name.StartsWith("NVIDIACorp", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Realtek", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AppUp.Intel", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("B9ECED6F", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AMD", StringComparison.OrdinalIgnoreCase))
            return true;

        // Codecs & Image Extensions
        if (name.Contains("VideoExtension", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ImageExtension", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WebMediaExtensions", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AVCEncoder", StringComparison.OrdinalIgnoreCase))
            return true;

        // Language & Speech
        if (name.Contains("LanguageExperiencePack", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Speech", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Handwriting", StringComparison.OrdinalIgnoreCase))
            return true;

        // System Frameworks & Core Windows Apps
        if (name.Contains("WinAppRuntime", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WidgetsPlatformRuntime", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ApplicationCompatibilityEnhancements", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Winget", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("StorePurchaseApp", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsCalculator", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsNotepad", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ScreenSketch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("VCLibs", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NET.Native", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("aimgr", StringComparison.OrdinalIgnoreCase))
            return true;

        // User installed desktop MSIX apps
        if (name.StartsWith("FastCopy", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("NotepadPlusPlus", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Agilebits", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("AdobeAcrobat", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Python", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("MEGASync", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("WindowsSubsystemForLinux", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string FormatFriendlyName(string raw)
    {
        if (raw.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
            raw = raw["Microsoft.".Length..];

        return raw switch
        {
            "BingWeather" => "MSN Weather",
            "BingNews" => "Microsoft News",
            "GetHelp" => "Get Help",
            "Getstarted" => "Tips",
            "MicrosoftFeedbackHub" => "Feedback Hub",
            "MicrosoftSolitaireCollection" => "Solitaire Collection",
            "YourPhone" => "Phone Link",
            "ZuneMusic" => "Media Player (Groove)",
            "ZuneVideo" => "Movies & TV",
            "WindowsAlarms" => "Alarms & Clock",
            "WindowsCamera" => "Camera",
            "WindowsMaps" => "Maps",
            "WindowsSoundRecorder" => "Sound Recorder",
            "549981C3F5F10" => "Cortana",
            "XboxApp" or "GamingApp" => "Xbox App",
            "XboxGamingOverlay" => "Xbox Game Bar",
            _ => raw
        };
    }

    public async Task<bool> UninstallUwpAppAsync(UwpAppItem item, Action<string>? onLog = null)
    {
        item.IsBusy = true;
        item.Status = "Uninstalling...";
        onLog?.Invoke($"[Debloat] Removing {item.DisplayName} ({item.Name})...");

        var script = $@"
            try {{
                Remove-AppxPackage -Package '{item.PackageFullName}' -ErrorAction SilentlyContinue
                Remove-AppxPackage -AllUsers -Package '{item.PackageFullName}' -ErrorAction SilentlyContinue
                Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -eq '{item.Name}' -or $_.PackageName -like '*{item.Name}*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
                Write-Output 'SUCCESS'
            }} catch {{
                Write-Error $_.Exception.Message
            }}
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        item.IsBusy = false;

        if (res.Success && res.Output.Contains("SUCCESS"))
        {
            item.Status = "Uninstalled";
            item.IsRemoved = true;
            onLog?.Invoke($"[Debloat] ✅ Successfully uninstalled {item.DisplayName}.");
            return true;
        }

        item.Status = "Failed";
        onLog?.Invoke($"[Debloat] ❌ Failed to uninstall {item.DisplayName}: {res.Error}");
        return false;
    }

    public async Task RunSfcScanAsync(Action<string> onLog, CancellationToken ct = default)
    {
        onLog("Starting System File Checker (sfc /scannow)...");
        await RunNativeCliAsync("sfc.exe", "/scannow", onLog, ct);
    }

    public async Task RunDismRestoreHealthAsync(Action<string> onLog, CancellationToken ct = default)
    {
        onLog("Starting DISM Component Store Repair (/Online /Cleanup-Image /RestoreHealth)...");
        await RunNativeCliAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", onLog, ct);
    }

    public async Task RunDismComponentCleanupAsync(Action<string> onLog, CancellationToken ct = default)
    {
        onLog("Starting WinSxS Component Store Deep Cleanup (/StartComponentCleanup /ResetBase)...");
        onLog("This reclaims superseded Windows updates and can free 5-15 GB of disk space.");
        await RunNativeCliAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup /ResetBase", onLog, ct);
    }

    public async Task FlushDnsAndResetNetworkAsync(Action<string> onLog)
    {
        onLog("Flushing DNS Resolver Cache (ipconfig /flushdns)...");
        await RunNativeCliAsync("ipconfig.exe", "/flushdns", onLog);

        onLog("Resetting Winsock Catalog (netsh winsock reset)...");
        await RunNativeCliAsync("netsh.exe", "winsock reset", onLog);

        onLog("Resetting TCP/IP Stack (netsh int ip reset)...");
        await RunNativeCliAsync("netsh.exe", "int ip reset", onLog);

        onLog("✅ Network cache and sockets reset successfully!");
    }

    public async Task<bool> SetDnsServersAsync(string dnsType, Action<string> onLog)
    {
        onLog($"Configuring DNS Servers to: {dnsType}...");

        string primary = "", secondary = "";
        switch (dnsType.ToLowerInvariant())
        {
            case "cloudflare":
                primary = "1.1.1.1";
                secondary = "1.0.0.1";
                break;
            case "google":
                primary = "8.8.8.8";
                secondary = "8.8.4.4";
                break;
            case "adguard":
                primary = "94.140.14.14";
                secondary = "94.140.15.15";
                break;
            case "quad9":
                primary = "9.9.9.9";
                secondary = "149.112.112.112";
                break;
            case "dhcp":
            default:
                var resetScript = @"
                    $adapters = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }
                    foreach ($adapter in $adapters) {
                        Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -ResetServerAddresses
                    }
                ";
                var rRes = await _powerShellService.ExecuteCommandAsync(resetScript);
                onLog("DNS reset to automatic DHCP dynamically.");
                return rRes.Success;
        }

        var script = $@"
            $adapters = Get-NetAdapter | Where-Object {{ $_.Status -eq 'Up' }}
            foreach ($adapter in $adapters) {{
                Set-DnsClientServerAddress -InterfaceIndex $adapter.InterfaceIndex -ServerAddresses ('{primary}', '{secondary}')
            }}
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success)
        {
            onLog($"✅ DNS configured to {dnsType} (Primary: {primary}, Secondary: {secondary})");
            return true;
        }

        onLog($"❌ Failed to update DNS: {res.Error}");
        return false;
    }

    public async Task<bool> CreateRestorePointAsync(string description, Action<string> onLog)
    {
        onLog($"Creating System Restore Point: '{description}'...");

        var script = $@"
            try {{
                Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                Write-Output 'SUCCESS'
            }} catch {{
                Write-Error $_.Exception.Message
            }}
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success && res.Output.Contains("SUCCESS"))
        {
            onLog("✅ System Restore Point created successfully!");
            return true;
        }

        onLog($"⚠️ Restore point creation reported: {res.Error} {res.Output}");
        return false;
    }

    private static async Task RunNativeCliAsync(string filename, string arguments, Action<string> onLog, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<int>();

        // Ensure full path to 64-bit Windows System32 is used to prevent WOW64 redirection issues
        var fullExePath = filename;
        if (!Path.IsPathRooted(filename))
        {
            var sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", filename);
            if (File.Exists(sys32))
            {
                fullExePath = sys32;
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = fullExePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.InstalledUICulture.TextInfo.OEMCodePage),
            StandardErrorEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.InstalledUICulture.TextInfo.OEMCodePage)
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                onLog(e.Data.Trim());
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                onLog($"[ERROR] {e.Data.Trim()}");
        };

        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        try
        {
            onLog($"[Execute] {Path.GetFileName(fullExePath)} {arguments}");
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var reg = ct.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { }
            });

            var exitCode = await tcs.Task;
            onLog($"Process completed with exit code: {exitCode}");
        }
        catch (Exception ex)
        {
            onLog($"Execution error for {filename}: {ex.Message}");
        }
    }

    // ==========================================
    // 1. Startup Applications Management
    // ==========================================
    public async Task<List<StartupItem>> GetStartupItemsAsync()
    {
        var items = new List<StartupItem>();

        var script = @"
            $results = @()

            # HKCU Run
            $hkcu = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
            if ($hkcu) {
                $hkcu.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
                    $results += [PSCustomObject]@{ Name = $_.Name; Command = $_.Value; Location = 'HKCU Run'; RegKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
                }
            }

            # HKLM Run
            $hklm = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
            if ($hklm) {
                $hklm.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
                    $results += [PSCustomObject]@{ Name = $_.Name; Command = $_.Value; Location = 'HKLM Run'; RegKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' }
                }
            }

            # Startup Folder
            $startupFolder = [Environment]::GetFolderPath('Startup')
            if (Test-Path $startupFolder) {
                Get-ChildItem -Path $startupFolder -Filter '*.lnk' -ErrorAction SilentlyContinue | ForEach-Object {
                    $results += [PSCustomObject]@{ Name = $_.BaseName; Command = $_.FullName; Location = 'Startup Folder'; RegKey = '' }
                }
            }

            $results | ConvertTo-Json -Compress
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
        {
            try
            {
                var raw = res.Output.Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(raw.StartsWith("[") ? raw : $"[{raw}]");
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    var cmd = el.TryGetProperty("Command", out var c) ? c.GetString() ?? "" : "";
                    var loc = el.TryGetProperty("Location", out var l) ? l.GetString() ?? "" : "";
                    var reg = el.TryGetProperty("RegKey", out var r) ? r.GetString() ?? "" : "";

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        items.Add(new StartupItem
                        {
                            Name = name,
                            Command = cmd,
                            Location = loc,
                            RegistryKey = reg,
                            IsEnabled = true,
                            Impact = "Active"
                        });
                    }
                }
            }
            catch { }
        }

        return items;
    }

    public async Task<bool> RemoveStartupItemAsync(StartupItem item)
    {
        if (item.Location == "Startup Folder")
        {
            try
            {
                if (File.Exists(item.Command))
                {
                    File.Delete(item.Command);
                    return true;
                }
            }
            catch { }
        }
        else if (!string.IsNullOrWhiteSpace(item.RegistryKey))
        {
            var script = $"Remove-ItemProperty -Path '{item.RegistryKey}' -Name '{item.Name}' -ErrorAction SilentlyContinue";
            var res = await _powerShellService.ExecuteCommandAsync(script);
            return res.Success;
        }

        return false;
    }

    // ==========================================
    // 2. Windows Background Services Management
    // ==========================================
    public async Task<List<ServiceItem>> GetServicesAsync(bool nonMicrosoftOnly = true)
    {
        var list = new List<ServiceItem>();

        var script = @"
            Get-CimInstance Win32_Service | Select-Object Name, DisplayName, State, StartMode, Description, PathName |
                ConvertTo-Json -Compress
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
        {
            try
            {
                var raw = res.Output.Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(raw.StartsWith("[") ? raw : $"[{raw}]");
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                    var disp = el.TryGetProperty("DisplayName", out var d) ? d.GetString() ?? "" : "";
                    var state = el.TryGetProperty("State", out var s) ? s.GetString() ?? "" : "";
                    var mode = el.TryGetProperty("StartMode", out var m) ? m.GetString() ?? "" : "";
                    var desc = el.TryGetProperty("Description", out var ds) ? ds.GetString() ?? "" : "";
                    var path = el.TryGetProperty("PathName", out var p) ? p.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var item = new ServiceItem
                    {
                        Name = name,
                        DisplayName = string.IsNullOrWhiteSpace(disp) ? name : disp,
                        Status = state,
                        StartupType = mode,
                        IsRunning = state.Equals("Running", StringComparison.OrdinalIgnoreCase),
                        Description = desc,
                        PathName = path
                    };

                    ServiceClassifier.Classify(item);
                    list.Add(item);
                }
            }
            catch { }
        }

        return list;
    }

    public async Task<bool> SetServiceStartupTypeAsync(ServiceItem item, string startupType)
    {
        item.IsBusy = true;
        var script = $"Set-Service -Name '{item.Name}' -StartupType {startupType} -ErrorAction SilentlyContinue";
        var res = await _powerShellService.ExecuteCommandAsync(script);
        item.IsBusy = false;

        if (res.Success)
        {
            item.StartupType = startupType;
            return true;
        }
        return false;
    }

    public async Task<bool> StartOrStopServiceAsync(ServiceItem item, bool start)
    {
        item.IsBusy = true;
        var cmd = start ? $"Start-Service -Name '{item.Name}'" : $"Stop-Service -Name '{item.Name}' -Force";
        var res = await _powerShellService.ExecuteCommandAsync(cmd);
        item.IsBusy = false;

        if (res.Success)
        {
            item.IsRunning = start;
            item.Status = start ? "Running" : "Stopped";
            return true;
        }
        return false;
    }

    // ==========================================
    // 3. Disk Space Analyzer
    // ==========================================
    public Task<List<DiskFolderItem>> AnalyzeDiskSpaceAsync(string rootPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var results = new List<DiskFolderItem>();
            if (!Directory.Exists(rootPath)) return results;

            var dirInfo = new DirectoryInfo(rootPath);
            long totalScannedBytes = 0;

            try
            {
                // Top subdirectories
                var subDirs = dirInfo.EnumerateDirectories().ToList();
                foreach (var dir in subDirs)
                {
                    if (ct.IsCancellationRequested) break;

                    long size = 0;
                    int count = 0;
                    try
                    {
                        foreach (var f in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, MaxRecursionDepth = 4 }))
                        {
                            if (ct.IsCancellationRequested) break;
                            size += f.Length;
                            count++;
                        }
                    }
                    catch { }

                    if (size > 0)
                    {
                        totalScannedBytes += size;
                        results.Add(new DiskFolderItem
                        {
                            Path = dir.FullName,
                            Name = dir.Name,
                            SizeBytes = size,
                            FormattedSize = FormatBytes(size),
                            IsDirectory = true,
                            FileCount = count
                        });
                    }
                }

                // Top large root files
                foreach (var f in dirInfo.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true }))
                {
                    if (ct.IsCancellationRequested) break;
                    totalScannedBytes += f.Length;
                    results.Add(new DiskFolderItem
                    {
                        Path = f.FullName,
                        Name = f.Name,
                        SizeBytes = f.Length,
                        FormattedSize = FormatBytes(f.Length),
                        IsDirectory = false,
                        FileCount = 1
                    });
                }

                // Calculate percentages, colors and sort descending
                var sorted = results.OrderByDescending(r => r.SizeBytes).ToList();
                var colors = new[]
                {
                    "#0284c7", "#0d9488", "#7c3aed", "#d97706", "#2563eb",
                    "#059669", "#c026d3", "#e11d48", "#4f46e5", "#0891b2",
                    "#ca8a04", "#475569"
                };

                if (totalScannedBytes > 0)
                {
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var item = sorted[i];
                        item.Percentage = Math.Round((double)item.SizeBytes / totalScannedBytes * 100.0, 1);
                        item.ColorHex = colors[i % colors.Length];
                    }
                }

                return sorted;
            }
            catch
            {
                return results;
            }
        }, ct);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB";
        if (bytes >= 1024L * 1024L)
            return $"{bytes / 1024.0 / 1024.0:0.0} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes} B";
    }

    // ==========================================
    // 4. Windows Optional Features Management
    // ==========================================
    private static readonly List<(string Feature, string Display, string Desc, string Cat)> CuratedFeatures = new()
    {
        ("Microsoft-Windows-Subsystem-Linux", "Windows Subsystem for Linux (WSL 2)", "Run native Linux distributions (Ubuntu, Debian, Fedora) directly inside Windows without virtual machine overhead.", "Virtualization & Developer"),
        ("VirtualMachinePlatform", "Virtual Machine Platform", "Core hypervisor support required for WSL 2, Android subsystem, and secure VM containers.", "Virtualization & Developer"),
        ("Containers-DisposableClientVM", "Windows Sandbox", "Isolated, temporary desktop environment to run untrusted software safely without affecting your host OS.", "Virtualization & Developer"),
        ("Microsoft-Hyper-V-All", "Hyper-V Virtualization", "Hardware-accelerated enterprise hypervisor to create and run virtual machines.", "Virtualization & Developer"),
        ("HypervisorPlatform", "Windows Hypervisor Platform", "Enables third-party virtualization software (VirtualBox, VMware) to utilize Windows hypervisor API.", "Virtualization & Developer"),
        ("DirectPlay", "DirectPlay (Legacy DirectX)", "Required for running classic 1990s and early 2000s PC games and legacy DirectX titles.", "Gaming & Legacy"),
        ("TelnetClient", "Telnet Client", "Command-line tool for testing network connectivity, open TCP ports, and communicating with servers.", "Networking & Admin"),
        ("TFTP", "TFTP Client", "Trivial File Transfer Protocol client used to transfer firmware or bootstrap files over UDP.", "Networking & Admin"),
        ("SMB1Protocol", "SMB 1.0 File Sharing Support", "Legacy file sharing protocol for connecting to very old NAS units or legacy Windows XP shares.", "Networking & Admin")
    };

    public async Task<List<OptionalFeatureItem>> GetOptionalFeaturesAsync()
    {
        var list = new List<OptionalFeatureItem>();

        var script = @"
            $features = Get-WindowsOptionalFeature -Online -ErrorAction SilentlyContinue
            if ($features) {
                $features | Select-Object FeatureName, State | ConvertTo-Json -Compress
            }
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        var stateMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
        {
            try
            {
                var raw = res.Output.Trim();
                using var doc = System.Text.Json.JsonDocument.Parse(raw.StartsWith("[") ? raw : $"[{raw}]");
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var fn = el.TryGetProperty("FeatureName", out var f) ? f.GetString() ?? "" : "";
                    var st = el.TryGetProperty("State", out var s) ? s.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(fn)) stateMap[fn] = st;
                }
            }
            catch { }
        }

        foreach (var (feature, display, desc, cat) in CuratedFeatures)
        {
            stateMap.TryGetValue(feature, out var state);
            bool isEnabled = state?.Equals("Enabled", StringComparison.OrdinalIgnoreCase) == true;

            list.Add(new OptionalFeatureItem
            {
                FeatureName = feature,
                DisplayName = display,
                Description = desc,
                Category = cat,
                IsEnabled = isEnabled,
                State = isEnabled ? "Enabled" : "Disabled"
            });
        }

        return list;
    }

    public async Task<bool> ToggleOptionalFeatureAsync(OptionalFeatureItem item, Action<string> onLog)
    {
        item.IsBusy = true;
        var willEnable = !item.IsEnabled;
        var actionText = willEnable ? "Enabling" : "Disabling";

        onLog($"{actionText} Windows feature '{item.DisplayName}'...");

        var script = willEnable
            ? $"Enable-WindowsOptionalFeature -Online -FeatureName '{item.FeatureName}' -All -NoRestart -ErrorAction Stop"
            : $"Disable-WindowsOptionalFeature -Online -FeatureName '{item.FeatureName}' -NoRestart -ErrorAction Stop";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        item.IsBusy = false;

        if (res.Success)
        {
            item.IsEnabled = willEnable;
            item.State = willEnable ? "Enabled" : "Disabled";
            onLog($"✅ Feature '{item.DisplayName}' is now {item.State}.");
            return true;
        }

        onLog($"❌ Failed to configure '{item.DisplayName}': {res.Error}");
        return false;
    }
}
