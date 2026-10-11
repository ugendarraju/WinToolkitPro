using System.Diagnostics;
using System.Text;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class SystemToolsService
{
    private readonly PowerShellService _powerShellService;

    private static readonly HashSet<string> KnownBloatwareNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.BingNews",
        "Microsoft.BingWeather",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.MicrosoftFeedbackHub",
        "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.People",
        "Microsoft.Todos",
        "Microsoft.WindowsAlarms",
        "Microsoft.WindowsCamera",
        "Microsoft.WindowsMaps",
        "Microsoft.WindowsSoundRecorder",
        "Microsoft.YourPhone",
        "Microsoft.ZuneMusic",
        "Microsoft.ZuneVideo",
        "Microsoft.XboxApp",
        "Microsoft.XboxGameOverlay",
        "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxIdentityProvider",
        "Microsoft.XboxSpeechToTextOverlay",
        "Clipchamp.Clipchamp",
        "SpotifyAB.SpotifyMusic",
        "Bytedance.TikTok",
        "Disney.DisneyPlus",
        "Microsoft.549981C3F5F10", // Cortana
        "Microsoft.Copilot",
        "Microsoft.GamingApp",
        "Microsoft.MixedReality.Portal",
        "Microsoft.3DBuilder",
        "Microsoft.MicrosoftStickyNotes"
    };

    public SystemToolsService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    public async Task<List<UwpAppItem>> ScanUwpAppsAsync(Action<string>? onLog = null)
    {
        onLog?.Invoke("Scanning installed Windows UWP packages...");

        var script = @"
            $apps = Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | 
                Select-Object Name, PackageFullName, Publisher
            $apps | ConvertTo-Json -Compress
        ";

        var result = await _powerShellService.ExecuteCommandAsync(script);
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
            onLog?.Invoke($"Parsing error during UWP scan: {ex.Message}");
        }

        onLog?.Invoke($"Found {list.Count} installed UWP application(s).");
        return list;
    }

    private void AddAppFromElement(System.Text.Json.JsonElement el, List<UwpAppItem> list)
    {
        var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
        var pkg = el.TryGetProperty("PackageFullName", out var p) ? p.GetString() ?? "" : "";
        var publisher = el.TryGetProperty("Publisher", out var pub) ? pub.GetString() ?? "" : "";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pkg)) return;

        var isBloat = KnownBloatwareNames.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(name, StringComparison.OrdinalIgnoreCase));
        var friendly = FormatFriendlyName(name);

        list.Add(new UwpAppItem
        {
            Name = name,
            DisplayName = friendly,
            PackageFullName = pkg,
            Publisher = publisher,
            IsBloatware = isBloat,
            IsSelected = isBloat
        });
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
                Remove-AppxPackage -Package '{item.PackageFullName}' -ErrorAction Stop
                Remove-AppxPackage -AllUsers -Package '{item.PackageFullName}' -ErrorAction SilentlyContinue
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
            case "automatic":
                break;
            default:
                primary = "1.1.1.1";
                secondary = "1.0.0.1";
                break;
        }

        string script;
        if (string.IsNullOrEmpty(primary))
        {
            script = @"
                Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object {
                    Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ResetServerAddresses
                }
                Write-Output 'DHCP_RESET_OK'
            ";
        }
        else
        {
            script = $@"
                Get-NetAdapter | Where-Object {{ $_.Status -eq 'Up' }} | ForEach-Object {{
                    Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ServerAddresses ('{primary}', '{secondary}')
                }}
                Write-Output 'DNS_SET_OK'
            ";
        }

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success)
        {
            onLog($"✅ DNS successfully set to {dnsType} ({(string.IsNullOrEmpty(primary) ? "Automatic DHCP" : primary + ", " + secondary)})");
            return true;
        }

        onLog($"❌ Failed to configure DNS: {res.Error}");
        return false;
    }

    public async Task<bool> CreateRestorePointAsync(string description, Action<string> onLog)
    {
        onLog($"Creating System Restore Point ('{description}')...");
        var script = $@"
            try {{
                Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'
                Write-Output 'RESTORE_POINT_OK'
            }} catch {{
                Write-Error $_.Exception.Message
            }}
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Success && res.Output.Contains("RESTORE_POINT_OK"))
        {
            onLog("✅ System Restore Point created successfully!");
            return true;
        }

        onLog($"Notice creating restore point: {res.Error}");
        return false;
    }

    private async Task RunNativeCliAsync(string exe, string args, Action<string> onLog, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLog(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLog($"[stderr] {e.Data}"); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } }))
            {
                await process.WaitForExitAsync(ct);
            }

            onLog($"Process finished with exit code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            onLog($"Error running {exe}: {ex.Message}");
        }
    }
}
