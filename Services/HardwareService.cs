using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class HardwareService
{
    private readonly PowerShellService _powerShellService;

    public HardwareService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    public async Task<HardwareInfo> GetHardwareInfoAsync()
    {
        var info = new HardwareInfo();

        try
        {
            // Query CPU, GPU, RAM, Motherboard, BIOS and Disks via PowerShell
            var script = @"
                $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1 Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, AddressWidth
                $gpus = Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, AdapterRAM
                $os = Get-CimInstance Win32_OperatingSystem | Select-Object TotalVisibleMemorySize, FreePhysicalMemory
                $ramSticks = Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity, Speed, MemoryType
                $physBytes = ($ramSticks | Measure-Object -Property Capacity -Sum).Sum
                $bb = Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer, Product
                $bios = Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion, ReleaseDate
                $disks = Get-CimInstance Win32_DiskDrive | Select-Object Model, InterfaceType, Size
                $battery = Get-CimInstance Win32_Battery | Select-Object EstimatedChargeRemaining, BatteryStatus

                [PSCustomObject]@{
                    CpuName = $cpu.Name
                    CpuCores = $cpu.NumberOfCores
                    CpuThreads = $cpu.NumberOfLogicalProcessors
                    CpuMaxClock = $cpu.MaxClockSpeed
                    CpuArch = $cpu.AddressWidth
                    GpuList = $gpus
                    PhysicalRamBytes = $physBytes
                    TotalRamKB = $os.TotalVisibleMemorySize
                    FreeRamKB = $os.FreePhysicalMemory
                    RamSpeed = ($ramSticks | Select-Object -First 1).Speed
                    RamCount = $ramSticks.Count
                    MbManufacturer = $bb.Manufacturer
                    MbProduct = $bb.Product
                    BiosVersion = $bios.SMBIOSBIOSVersion
                    BiosDate = $bios.ReleaseDate
                    DiskList = $disks
                    BatteryPercent = $battery.EstimatedChargeRemaining
                    HasBattery = ($battery -ne $null)
                } | ConvertTo-Json -Depth 3 -Compress
            ";

            var res = await _powerShellService.ExecuteCommandAsync(script);
            if (res.Success && !string.IsNullOrWhiteSpace(res.Output))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(res.Output.Trim());
                var root = doc.RootElement;

                // CPU
                if (root.TryGetProperty("CpuName", out var cn)) info.CpuName = cn.GetString() ?? "Unknown";
                if (root.TryGetProperty("CpuCores", out var cc)) info.CpuCores = cc.GetInt32();
                if (root.TryGetProperty("CpuThreads", out var ct)) info.CpuThreads = ct.GetInt32();
                if (root.TryGetProperty("CpuMaxClock", out var cmc))
                {
                    var mhz = cmc.GetInt32();
                    info.CpuClockSpeed = mhz > 1000 ? $"{(mhz / 1000.0):0.00} GHz" : $"{mhz} MHz";
                }
                if (root.TryGetProperty("CpuArch", out var ca)) info.CpuArchitecture = $"{ca.GetInt32()}-bit";

                // GPU
                if (root.TryGetProperty("GpuList", out var gpus))
                {
                    if (gpus.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var primary = gpus.EnumerateArray().LastOrDefault();
                        SetGpuDetails(primary, info);
                    }
                    else if (gpus.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        SetGpuDetails(gpus, info);
                    }
                }

                // RAM
                long physBytes = 0;
                if (root.TryGetProperty("PhysicalRamBytes", out var prb) && prb.TryGetInt64(out var pb))
                {
                    physBytes = pb;
                }

                if (root.TryGetProperty("TotalRamKB", out var tr) && tr.TryGetInt64(out var totalKb))
                {
                    var usableGb = totalKb / 1024.0 / 1024.0;
                    var installedGb = physBytes > 0 
                        ? Math.Round(physBytes / 1024.0 / 1024.0 / 1024.0) 
                        : Math.Round(usableGb);

                    info.RamTotal = $"{installedGb} GB";

                    if (root.TryGetProperty("FreeRamKB", out var fr) && fr.TryGetInt64(out var freeKb))
                    {
                        var availGb = freeKb / 1024.0 / 1024.0;
                        var usedPct = Math.Clamp((int)Math.Round((usableGb - availGb) / usableGb * 100.0), 0, 100);
                        info.RamAvailable = $"{availGb:0.0} GB Free ({usedPct}% used)";
                    }
                }
                if (root.TryGetProperty("RamSpeed", out var rs) && rs.TryGetInt32(out var spd) && spd > 0)
                {
                    info.RamSpeed = $"{spd} MHz";
                }
                if (root.TryGetProperty("RamCount", out var rc))
                {
                    info.RamSlots = $"{rc.GetInt32()} module(s) installed";
                }

                // Motherboard & BIOS
                if (root.TryGetProperty("MbManufacturer", out var mbm)) info.MotherboardManufacturer = mbm.GetString() ?? "";
                if (root.TryGetProperty("MbProduct", out var mbp)) info.MotherboardModel = mbp.GetString() ?? "";
                if (root.TryGetProperty("BiosVersion", out var bv)) info.BiosVersion = bv.GetString() ?? "";

                // Storage Drives
                var driveInfos = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
                foreach (var d in driveInfos)
                {
                    var totalGb = d.TotalSize / 1024.0 / 1024.0 / 1024.0;
                    var freeGb = d.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
                    var usedPct = (totalGb - freeGb) / totalGb * 100.0;

                    info.Disks.Add(new DiskDriveInfo
                    {
                        Name = d.Name,
                        Model = $"{d.VolumeLabel} ({d.DriveFormat})",
                        InterfaceType = "Fixed Drive",
                        TotalSize = $"{totalGb:0.0} GB",
                        FreeSize = $"{freeGb:0.0} GB",
                        UsedPercent = usedPct
                    });
                }

                // Battery
                if (root.TryGetProperty("HasBattery", out var hb) && hb.GetBoolean())
                {
                    info.HasBattery = true;
                    if (root.TryGetProperty("BatteryPercent", out var bp))
                    {
                        info.BatteryPercentage = bp.GetInt32();
                    }

                    await PopulateBatteryWearDetailsAsync(info);
                }
            }
        }
        catch { }

        return info;
    }

    private static void SetGpuDetails(System.Text.Json.JsonElement gpu, HardwareInfo info)
    {
        if (gpu.TryGetProperty("Name", out var gn)) info.GpuName = gn.GetString() ?? "Unknown";
        if (gpu.TryGetProperty("DriverVersion", out var gv)) info.GpuDriverVersion = gv.GetString() ?? "";
        if (gpu.TryGetProperty("AdapterRAM", out var gr) && gr.TryGetInt64(out var ramBytes) && ramBytes > 0)
        {
            info.GpuVram = $"{ramBytes / 1024 / 1024 / 1024} GB Dedicated";
        }
    }

    private async Task PopulateBatteryWearDetailsAsync(HardwareInfo info)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "WinToolkit_battery.html");
            await _powerShellService.ExecuteCommandAsync($"powercfg /batteryreport /output \"{tempPath}\"");

            if (File.Exists(tempPath))
            {
                var html = await File.ReadAllTextAsync(tempPath);

                var designMatch = Regex.Match(html, @"DESIGN CAPACITY[\s\S]*?<td>([\d,]+)\s*mWh", RegexOptions.IgnoreCase);
                var fullMatch = Regex.Match(html, @"FULL CHARGE CAPACITY[\s\S]*?<td>([\d,]+)\s*mWh", RegexOptions.IgnoreCase);

                if (designMatch.Success && fullMatch.Success)
                {
                    var designStr = designMatch.Groups[1].Value.Replace(",", "");
                    var fullStr = fullMatch.Groups[1].Value.Replace(",", "");

                    if (double.TryParse(designStr, out var design) && double.TryParse(fullStr, out var full) && design > 0)
                    {
                        info.BatteryDesignCapacity = $"{design:N0} mWh";
                        info.BatteryFullChargeCapacity = $"{full:N0} mWh";
                        var health = (full / design) * 100.0;
                        info.BatteryHealthPercent = Math.Min(100.0, Math.Round(health, 1));

                        info.BatteryHealthGrade = info.BatteryHealthPercent switch
                        {
                            >= 85 => "Excellent",
                            >= 70 => "Good",
                            >= 50 => "Moderate (Wear Detected)",
                            _ => "Degraded (Replacement Recommended)"
                        };
                    }
                }
            }
        }
        catch { }
    }

    public async Task<string> GenerateAndOpenBatteryReportAsync()
    {
        var outPath = Path.Combine(Path.GetTempPath(), "WindowsBatteryReport.html");
        await _powerShellService.ExecuteCommandAsync($"powercfg /batteryreport /output \"{outPath}\"");

        if (File.Exists(outPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = outPath,
                UseShellExecute = true
            });
            return outPath;
        }

        return string.Empty;
    }
}
