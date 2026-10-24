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
                $ramSticks = Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity, Speed, MemoryType, SMBIOSMemoryType
                $physBytes = ($ramSticks | Measure-Object -Property Capacity -Sum).Sum
                $firstRam = $ramSticks | Select-Object -First 1
                $bb = Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer, Product
                $bios = Get-CimInstance Win32_BIOS | Select-Object SMBIOSBIOSVersion, ReleaseDate
                $disks = Get-CimInstance Win32_DiskDrive | Select-Object Model, InterfaceType, Size
                $battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Select-Object -First 1 EstimatedChargeRemaining, BatteryStatus
                $hasBat = ($battery -ne $null -and $battery.EstimatedChargeRemaining -ne $null)

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
                    RamSpeed = $firstRam.Speed
                    RamSmbiosType = $firstRam.SMBIOSMemoryType
                    RamMemType = $firstRam.MemoryType
                    RamCount = $ramSticks.Count
                    MbManufacturer = $bb.Manufacturer
                    MbProduct = $bb.Product
                    BiosVersion = $bios.SMBIOSBIOSVersion
                    BiosDate = $bios.ReleaseDate
                    DiskList = $disks
                    BatteryPercent = if ($hasBat) { $battery.EstimatedChargeRemaining } else { 0 }
                    HasBattery = $hasBat
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
                // Detect DDR Generation (SMBIOS Memory Type or speed heuristics)
                var ramGeneration = string.Empty;
                var smbiosType = 0;
                var memType = 0;
                if (root.TryGetProperty("RamSmbiosType", out var rst) && rst.TryGetInt32(out var st)) smbiosType = st;
                if (root.TryGetProperty("RamMemType", out var rmt) && rmt.TryGetInt32(out var mt)) memType = mt;

                ramGeneration = smbiosType switch
                {
                    19 => "DDR",
                    20 => "DDR2",
                    21 => "DDR2 FB-DIMM",
                    24 => "DDR3",
                    26 => "DDR4",
                    27 => "LPDDR",
                    28 => "LPDDR2",
                    29 => "LPDDR3",
                    30 => "LPDDR4",
                    31 => "Logical non-volatile device",
                    34 => "DDR5",
                    35 => "LPDDR5",
                    _ => string.Empty
                };

                if (string.IsNullOrEmpty(ramGeneration))
                {
                    ramGeneration = memType switch
                    {
                        19 => "DDR",
                        20 => "DDR2",
                        21 => "DDR2 FB-DIMM",
                        24 => "DDR3",
                        26 => "DDR4",
                        _ => string.Empty
                    };
                }

                int speedMhz = 0;
                if (root.TryGetProperty("RamSpeed", out var rs) && rs.TryGetInt32(out var spd) && spd > 0)
                {
                    speedMhz = spd;
                    info.RamSpeed = $"{spd} MHz";
                }

                // Fallback heuristic based on speed if SMBIOS was 0/unknown
                if (string.IsNullOrEmpty(ramGeneration) && speedMhz > 0)
                {
                    if (speedMhz >= 4800) ramGeneration = "DDR5";
                    else if (speedMhz >= 2133) ramGeneration = "DDR4";
                    else if (speedMhz >= 800) ramGeneration = "DDR3";
                    else if (speedMhz >= 400) ramGeneration = "DDR2";
                }

                info.RamType = ramGeneration;
                if (!string.IsNullOrEmpty(ramGeneration))
                {
                    info.RamTotal = $"{info.RamTotal} {ramGeneration}".Trim();
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
                    if (root.TryGetProperty("BatteryPercent", out var bp) && bp.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        info.BatteryPercentage = bp.GetInt32();
                    }

                    await PopulateBatteryWearDetailsAsync(info);
                }
                else
                {
                    info.HasBattery = false;
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

    public static string GetPowerCfgPath()
    {
        if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            var sysnativePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "sysnative", "powercfg.exe");
            if (File.Exists(sysnativePath))
            {
                return sysnativePath;
            }
        }
        else
        {
            var system32Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "powercfg.exe");
            if (File.Exists(system32Path))
            {
                return system32Path;
            }
        }
        return "powercfg.exe";
    }

    public static void ParseBatteryHtml(string html, HardwareInfo info)
    {
        var designMatch = Regex.Match(html, @"DESIGN CAPACITY[\s\S]*?<td[^>]*>([\d,]+)\s*mWh", RegexOptions.IgnoreCase);
        var fullMatch = Regex.Match(html, @"FULL CHARGE CAPACITY[\s\S]*?<td[^>]*>([\d,]+)\s*mWh", RegexOptions.IgnoreCase);
        var cycleMatch = Regex.Match(html, @"CYCLE COUNT[\s\S]*?<td[^>]*>\s*([\d,]+|-)\s*<\/td>", RegexOptions.IgnoreCase);

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

        if (cycleMatch.Success)
        {
            var val = cycleMatch.Groups[1].Value.Trim();
            info.BatteryCycleCount = (val == "-" || string.IsNullOrWhiteSpace(val)) ? "N/A" : val;
        }
    }

    private async Task PopulateBatteryWearDetailsAsync(HardwareInfo info)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WinToolkit");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, "temp_battery.html");

            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

            var psi = new ProcessStartInfo
            {
                FileName = GetPowerCfgPath(),
                Arguments = $"/batteryreport /output \"{tempPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }

            if (File.Exists(tempPath))
            {
                var html = await File.ReadAllTextAsync(tempPath);
                try { File.Delete(tempPath); } catch { }

                ParseBatteryHtml(html, info);
            }
            else
            {
                var existingReport = Path.Combine(tempDir, "WindowsBatteryReport.html");
                if (File.Exists(existingReport))
                {
                    var html = await File.ReadAllTextAsync(existingReport);
                    ParseBatteryHtml(html, info);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Battery wear details error: {ex.Message}");
        }
    }

    public async Task<string> GenerateAndOpenBatteryReportAsync()
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WinToolkit");
            Directory.CreateDirectory(tempDir);
            var outPath = Path.Combine(tempDir, "WindowsBatteryReport.html");

            try
            {
                if (File.Exists(outPath)) File.Delete(outPath);
            }
            catch { }

            var psi = new ProcessStartInfo
            {
                FileName = GetPowerCfgPath(),
                Arguments = $"/batteryreport /output \"{outPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }

            if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
            {
                bool launched = false;

                // 1. Try default system browser via shell execute
                try
                {
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = outPath,
                        UseShellExecute = true
                    });
                    if (p != null) launched = true;
                }
                catch { }

                // 2. Direct browser executables search across both 64-bit and 32-bit program directories
                if (!launched)
                {
                    var baseDirs = new List<string>();
                    var p64 = Environment.GetEnvironmentVariable("ProgramW6432");
                    if (!string.IsNullOrEmpty(p64) && !baseDirs.Contains(p64, StringComparer.OrdinalIgnoreCase))
                    {
                        baseDirs.Add(p64);
                    }

                    var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    if (!string.IsNullOrEmpty(pf) && !baseDirs.Contains(pf, StringComparer.OrdinalIgnoreCase))
                    {
                        baseDirs.Add(pf);
                    }

                    var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    if (!string.IsNullOrEmpty(pfx86) && !baseDirs.Contains(pfx86, StringComparer.OrdinalIgnoreCase))
                    {
                        baseDirs.Add(pfx86);
                    }

                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (!string.IsNullOrEmpty(localAppData) && !baseDirs.Contains(localAppData, StringComparer.OrdinalIgnoreCase))
                    {
                        baseDirs.Add(localAppData);
                    }

                    var relativeBrowserPaths = new[]
                    {
                        @"Google\Chrome\Application\chrome.exe",
                        @"Microsoft\Edge\Application\msedge.exe",
                        @"Mozilla Firefox\firefox.exe",
                        @"BraveSoftware\Brave-Browser\Application\brave.exe",
                        @"Programs\Opera\launcher.exe",
                        @"Programs\Opera GX\launcher.exe",
                        @"Vivaldi\Application\vivaldi.exe"
                    };

                    foreach (var dir in baseDirs)
                    {
                        foreach (var rel in relativeBrowserPaths)
                        {
                            var fullPath = Path.Combine(dir, rel);
                            if (File.Exists(fullPath))
                            {
                                try
                                {
                                    var bProc = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = fullPath,
                                        Arguments = $"\"{outPath}\"",
                                        UseShellExecute = false
                                    });
                                    if (bProc != null)
                                    {
                                        launched = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        if (launched) break;
                    }
                }

                // 3. Fallback to explorer.exe
                if (!launched)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{outPath}\"",
                            UseShellExecute = false
                        });
                        launched = true;
                    }
                    catch { }
                }

                // 4. Fallback to cmd.exe start
                if (!launched)
                {
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c start \"\" \"{outPath}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        p?.WaitForExit(2000);
                        launched = true;
                    }
                    catch { }
                }

                return outPath;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Battery report error: {ex.Message}");
        }

        return string.Empty;
    }
}
