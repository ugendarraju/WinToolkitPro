using System.Text.Json;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class DriverService
{
    private readonly PowerShellService _powerShellService;

    public DriverService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    /// <summary>
    /// Scans the system for installed hardware devices and problematic/missing drivers.
    /// </summary>
    public async Task<List<DriverItem>> ScanDevicesAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Scanning hardware devices and driver states...");

        var script = @"
            $devices = Get-CimInstance Win32_PnPEntity | Where-Object { 
                $_.PNPClass -in @('Display', 'Net', 'MEDIA', 'Bluetooth', 'System', 'Storage', 'Processor') -and $_.Name -ne $null
            } | Select-Object -First 40 Name, PNPClass, Manufacturer, DeviceID, Status, ConfigManagerErrorCode

            $result = foreach ($d in $devices) {
                $hasIssue = ($d.ConfigManagerErrorCode -ne 0 -or $d.Status -ne 'OK')
                [PSCustomObject]@{
                    DeviceName = $d.Name
                    DeviceClass = if ($d.PNPClass) { $d.PNPClass } else { 'System' }
                    Manufacturer = if ($d.Manufacturer) { $d.Manufacturer } else { 'Unknown' }
                    HardwareId = $d.DeviceID
                    Status = if ($hasIssue) { 'Needs Attention / Missing Driver' } else { 'Healthy' }
                    HasUpdate = $hasIssue
                }
            }

            $result | ConvertTo-Json -Compress
        ";

        var (success, output, error) = await _powerShellService.ExecuteCommandAsync(script, cancellationToken: cancellationToken);

        var list = new List<DriverItem>();
        if (!success || string.IsNullOrWhiteSpace(output))
        {
            onLog?.Invoke($"Device scan finished with notice: {error}");
            return list;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    list.Add(ParseDriverFromJson(element));
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                list.Add(ParseDriverFromJson(doc.RootElement));
            }
            onLog?.Invoke($"Found {list.Count} hardware devices.");
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Error parsing hardware list: {ex.Message}");
        }

        return list;
    }

    /// <summary>
    /// Checks Microsoft Windows Update Catalog for driver updates using the Windows Update Agent COM API.
    /// </summary>
    public async Task<List<DriverItem>> CheckOnlineDriverUpdatesAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Contacting Windows Update Catalog for certified driver updates...");

        var script = @"
            try {
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.ServerSelection = 2 # Windows Update Catalog
                $searchResult = $searcher.Search(""IsInstalled=0 and Type='Driver'"")
                
                $updates = @()
                foreach ($update in $searchResult.Updates) {
                    $updates += [PSCustomObject]@{
                        DeviceName = $update.Title
                        DeviceClass = 'Driver Update'
                        Manufacturer = if ($update.DriverModel) { $update.DriverModel } else { 'Microsoft Catalog' }
                        HardwareId = $update.Identity.UpdateID
                        Status = 'Update Available'
                        HasUpdate = $true
                    }
                }

                if ($updates.Count -gt 0) {
                    $updates | ConvertTo-Json -Compress
                } else {
                    '[]'
                }
            } catch {
                Write-Error $_.Exception.Message
                '[]'
            }
        ";

        var (success, output, error) = await _powerShellService.ExecuteCommandAsync(script, cancellationToken: cancellationToken);
        var list = new List<DriverItem>();

        if (!string.IsNullOrWhiteSpace(output))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        list.Add(ParseDriverFromJson(element));
                    }
                }
            }
            catch { }
        }

        onLog?.Invoke(list.Count > 0 
            ? $"Found {list.Count} certified driver update(s) online!" 
            : "All drivers are up to date in the Windows Update Catalog.");

        return list;
    }

    /// <summary>
    /// Downloads and installs driver updates one by one with real-time per-driver event notifications.
    /// </summary>
    public async Task<bool> InstallDriverUpdatesAsync(
        Action<string>? onLog = null,
        Action<int, string, string>? onDriverEvent = null,
        CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Starting step-by-step driver installation engine...");

        var script = @"
            $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if (-not $isAdmin) {
                Write-Output 'DRIVER_ERROR|ADMIN_REQUIRED|Administrator privileges required to install drivers. Please click Elevate to Admin.'
                exit 1
            }

            try {
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.ServerSelection = 2
                $searchResult = $searcher.Search(""IsInstalled=0 and Type='Driver'"")

                $count = $searchResult.Updates.Count
                if ($count -eq 0) {
                    Write-Output 'No driver updates pending.'
                    exit 0
                }

                Write-Output ""Processing $count driver updates sequentially...""

                $successCount = 0
                $failCount = 0

                for ($i = 0; $i -lt $count; $i++) {
                    $update = $searchResult.Updates.Item($i)
                    $title = $update.Title

                    # Single-item update collection for download
                    $singleColl = New-Object -ComObject Microsoft.Update.UpdateColl
                    $singleColl.Add($update) | Out-Null

                    # 1. Download
                    Write-Output ""DRIVER_EVENT|$i|DOWNLOADING|$title""
                    $downloader = $session.CreateUpdateDownloader()
                    $downloader.Updates = $singleColl
                    $downRes = $downloader.Download()

                    if ($downRes.ResultCode -ne 2 -and $downRes.ResultCode -ne 3) {
                        Write-Output ""DRIVER_EVENT|$i|FAILED|$title|Download failed with code $($downRes.ResultCode)""
                        $failCount++
                        continue
                    }

                    # 2. Install
                    Write-Output ""DRIVER_EVENT|$i|INSTALLING|$title""
                    $installer = $session.CreateUpdateInstaller()
                    $installer.Updates = $singleColl
                    $instRes = $installer.Install()

                    # Result Codes: 2 = Succeeded, 3 = SucceededWithErrors, 4 = Failed, 5 = Aborted
                    if ($instRes.ResultCode -eq 2 -or $instRes.ResultCode -eq 3) {
                        Write-Output ""DRIVER_EVENT|$i|SUCCESS|$title""
                        $successCount++
                    } else {
                        $updRes = $instRes.GetUpdateResult(0)
                        $hres = $updRes.HResult
                        Write-Output ""DRIVER_EVENT|$i|FAILED|$title|Install failed (Error: 0x$($hres.ToString('X')) - Code: $($instRes.ResultCode))""
                        $failCount++
                    }
                }

                Write-Output ""DRIVER_SUMMARY|$successCount|$failCount""
                exit 0
            } catch {
                Write-Output ""DRIVER_ERROR|EXCEPTION|$($_.Exception.Message)""
                exit 1
            }
        ";

        var (success, _, _) = await _powerShellService.ExecuteCommandAsync(
            script,
            onOutputLine: line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                if (line.StartsWith("DRIVER_EVENT|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4 && int.TryParse(parts[1], out var idx))
                    {
                        var stage = parts[2];
                        var detail = parts.Length > 4 ? parts[4] : parts[3];
                        onDriverEvent?.Invoke(idx, stage, detail);
                    }
                }
                else if (line.StartsWith("DRIVER_SUMMARY|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        onLog?.Invoke($"🎯 Installation Complete: {parts[1]} driver(s) succeeded, {parts[2]} failed.");
                    }
                }
                else if (line.StartsWith("DRIVER_ERROR|"))
                {
                    var parts = line.Split('|');
                    var msg = parts.Length > 2 ? parts[2] : parts[1];
                    onLog?.Invoke($"⚠️ ERROR: {msg}");
                }
                else
                {
                    onLog?.Invoke(line);
                }
            },
            cancellationToken: cancellationToken);

        return success;
    }

    private static DriverItem ParseDriverFromJson(JsonElement element)
    {
        var name = element.TryGetProperty("DeviceName", out var n) ? n.GetString() ?? "Device" : "Device";
        var pnpClass = element.TryGetProperty("DeviceClass", out var c) ? c.GetString() ?? "System" : "System";
        var mfr = element.TryGetProperty("Manufacturer", out var m) ? m.GetString() ?? "Unknown" : "Unknown";
        var hwId = element.TryGetProperty("HardwareId", out var h) ? h.GetString() ?? "" : "";
        var status = element.TryGetProperty("Status", out var s) ? s.GetString() ?? "Update Available" : "Update Available";
        var hasUpd = element.TryGetProperty("HasUpdate", out var u) && u.GetBoolean();

        return new DriverItem
        {
            DeviceName = name,
            DeviceClass = pnpClass,
            Manufacturer = mfr,
            HardwareId = hwId,
            Status = status,
            StatusMessage = "Ready to install",
            HasUpdate = hasUpd,
            IsSelected = true
        };
    }
}
