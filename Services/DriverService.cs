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
            ? $"Found {list.Count} driver update(s) online!" 
            : "All drivers are up to date in the Windows Update Catalog.");

        return list;
    }

    /// <summary>
    /// Downloads and installs driver updates automatically through Windows Update Agent.
    /// </summary>
    public async Task<bool> InstallDriverUpdatesAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Beginning silent download and installation of certified driver updates...");

        var script = @"
            try {
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.ServerSelection = 2
                $searchResult = $searcher.Search(""IsInstalled=0 and Type='Driver'"")

                if ($searchResult.Updates.Count -eq 0) {
                    Write-Output 'No driver updates pending.'
                    exit 0
                }

                Write-Output ""Downloading $($searchResult.Updates.Count) driver updates...""
                $downloader = $session.CreateUpdateDownloader()
                $downloader.Updates = $searchResult.Updates
                $downloader.Download()

                Write-Output 'Installing driver updates silently...'
                $installer = $session.CreateUpdateInstaller()
                $installer.Updates = $searchResult.Updates
                $installResult = $installer.Install()

                Write-Output ""Installation Result Code: $($installResult.ResultCode)""
                exit 0
            } catch {
                Write-Error $_.Exception.Message
                exit 1
            }
        ";

        var (success, _, _) = await _powerShellService.ExecuteCommandAsync(
            script,
            onOutputLine: line => onLog?.Invoke(line),
            cancellationToken: cancellationToken);

        return success;
    }

    private static DriverItem ParseDriverFromJson(JsonElement element)
    {
        var name = element.TryGetProperty("DeviceName", out var n) ? n.GetString() ?? "Device" : "Device";
        var pnpClass = element.TryGetProperty("DeviceClass", out var c) ? c.GetString() ?? "System" : "System";
        var mfr = element.TryGetProperty("Manufacturer", out var m) ? m.GetString() ?? "Unknown" : "Unknown";
        var hwId = element.TryGetProperty("HardwareId", out var h) ? h.GetString() ?? "" : "";
        var status = element.TryGetProperty("Status", out var s) ? s.GetString() ?? "Healthy" : "Healthy";
        var hasUpd = element.TryGetProperty("HasUpdate", out var u) && u.GetBoolean();

        return new DriverItem
        {
            DeviceName = name,
            DeviceClass = pnpClass,
            Manufacturer = mfr,
            HardwareId = hwId,
            Status = status,
            HasUpdate = hasUpd,
            IsSelected = hasUpd
        };
    }
}
