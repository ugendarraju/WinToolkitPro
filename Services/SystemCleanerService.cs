using System.IO;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class SystemCleanerService
{
    private readonly PowerShellService _powerShellService;

    public SystemCleanerService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    public List<CleanerTargetItem> GetDefaultTargets()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var tempPath = Path.GetTempPath();

        return new List<CleanerTargetItem>
        {
            // Windows System
            new()
            {
                Id = "win_temp",
                Name = "Windows Temporary Files",
                Category = "Windows System",
                Description = "Temporary files created by Windows and running background processes.",
                PathPatterns = new() { tempPath, Path.Combine(winDir, "Temp") }
            },
            new()
            {
                Id = "win_recycle",
                Name = "Empty Recycle Bin",
                Category = "Windows System",
                Description = "Permanently purge deleted files stored in the Windows Recycle Bin.",
                PathPatterns = new() { @"C:\$Recycle.Bin" }
            },
            new()
            {
                Id = "win_prefetch",
                Name = "Windows Prefetch & Memory Dumps",
                Category = "Windows System",
                Description = "Stale execution prefetch caches and operating system memory crash dumps.",
                PathPatterns = new() { Path.Combine(winDir, "Prefetch"), Path.Combine(winDir, "Minidump") }
            },
            new()
            {
                Id = "win_logs",
                Name = "Windows Diagnostic & Setup Logs",
                Category = "Windows System",
                Description = "Old Windows log files, CBS logs, DISM logs, and setup report caches.",
                PathPatterns = new() { Path.Combine(winDir, "Logs"), Path.Combine(winDir, "Panther") }
            },
            new()
            {
                Id = "win_update_cache",
                Name = "Windows SoftwareDistribution Download Cache",
                Category = "Windows System",
                Description = "Old downloaded Windows Update installer packages already applied.",
                PathPatterns = new() { Path.Combine(winDir, "SoftwareDistribution", "Download") }
            },

            // Web Browsers
            new()
            {
                Id = "chrome_cache",
                Name = "Google Chrome Cache",
                Category = "Web Browsers",
                Description = "Web content cache, media cache, and temporary GPU shader storage for Chrome.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"),
                    Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Code Cache")
                }
            },
            new()
            {
                Id = "edge_cache",
                Name = "Microsoft Edge Cache",
                Category = "Web Browsers",
                Description = "Web content cache, icon cache, and temporary buffers for Microsoft Edge.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                    Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Code Cache")
                }
            },
            new()
            {
                Id = "firefox_cache",
                Name = "Mozilla Firefox Cache",
                Category = "Web Browsers",
                Description = "Firefox browser disk cache and temporary offline page resources.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"Mozilla\Firefox\Profiles")
                }
            },
            new()
            {
                Id = "brave_cache",
                Name = "Brave Browser Cache",
                Category = "Web Browsers",
                Description = "Web cache and code cache for Brave Browser.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                    Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache")
                }
            },

            // Application Caches
            new()
            {
                Id = "discord_cache",
                Name = "Discord Cache & GPU Cache",
                Category = "Application Caches",
                Description = "Cached voice/media attachments and GPU shader buffers from Discord.",
                PathPatterns = new()
                {
                    Path.Combine(appData, @"discord\Cache"),
                    Path.Combine(appData, @"discord\Code Cache")
                }
            },
            new()
            {
                Id = "spotify_cache",
                Name = "Spotify Local Cache",
                Category = "Application Caches",
                Description = "Local song stream cache and album art buffers.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"Spotify\Storage"),
                    Path.Combine(localAppData, @"Spotify\Data")
                }
            },
            new()
            {
                Id = "vscode_cache",
                Name = "VS Code & IDE Caches",
                Category = "Application Caches",
                Description = "Cached extension logs, language server indexes, and workspace backups.",
                PathPatterns = new()
                {
                    Path.Combine(appData, @"Code\Cache"),
                    Path.Combine(appData, @"Code\CachedData")
                }
            },
            new()
            {
                Id = "package_caches",
                Name = "Package Manager Caches (Pip, NPM, Nuget, Winget)",
                Category = "Application Caches",
                Description = "Downloaded archive caches from Node NPM, Python pip, NuGet, and WinGet.",
                PathPatterns = new()
                {
                    Path.Combine(localAppData, @"pip\cache"),
                    Path.Combine(appData, @"npm-cache"),
                    Path.Combine(localAppData, @"NuGet\v3-cache"),
                    Path.Combine(tempPath, "WinGet")
                }
            }
        };
    }

    /// <summary>
    /// Analyzes the system to calculate junk file sizes for each target.
    /// </summary>
    public async Task AnalyzeTargetsAsync(
        IEnumerable<CleanerTargetItem> targets,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Analyzing system junk, caches, and temporary files...");

        await Task.Run(() =>
        {
            foreach (var target in targets)
            {
                if (cancellationToken.IsCancellationRequested) break;

                long totalBytes = 0;
                int totalCount = 0;

                if (target.Id == "win_recycle")
                {
                    // Special check for Recycle bin
                    var drive = new DriveInfo("C");
                    // We can estimate or query recycle bin
                    target.BytesFound = GetRecycleBinSize();
                    target.ItemsFound = target.BytesFound > 0 ? 1 : 0;
                    target.Status = $"{target.FormattedSize} to clean";
                    continue;
                }

                foreach (var path in target.PathPatterns)
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;

                    try
                    {
                        if (Directory.Exists(path))
                        {
                            var dirInfo = new DirectoryInfo(path);
                            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                if (cancellationToken.IsCancellationRequested) break;
                                try
                                {
                                    totalBytes += file.Length;
                                    totalCount++;
                                }
                                catch { }
                            }
                        }
                        else if (File.Exists(path))
                        {
                            var fi = new FileInfo(path);
                            totalBytes += fi.Length;
                            totalCount++;
                        }
                    }
                    catch { }
                }

                target.BytesFound = totalBytes;
                target.ItemsFound = totalCount;
                target.Status = totalBytes > 0 ? $"{target.FormattedSize} junk found" : "Clean";
            }
        }, cancellationToken);

        onLog?.Invoke("Junk analysis complete.");
    }

    /// <summary>
    /// Cleans the selected junk targets safely.
    /// </summary>
    public async Task<long> CleanTargetsAsync(
        IEnumerable<CleanerTargetItem> targets,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Starting CCleaner Pro cleanup routine...");
        long cleanedBytes = 0;

        await Task.Run(async () =>
        {
            foreach (var target in targets.Where(t => t.IsSelected))
            {
                if (cancellationToken.IsCancellationRequested) break;

                target.IsBusy = true;
                target.Status = "Cleaning...";
                onLog?.Invoke($"[Cleaning] {target.Name}...");

                long targetCleaned = 0;

                if (target.Id == "win_recycle")
                {
                    try
                    {
                        await _powerShellService.ExecuteCommandAsync("Clear-RecycleBin -Force -ErrorAction SilentlyContinue", cancellationToken: cancellationToken);
                        targetCleaned = target.BytesFound;
                        onLog?.Invoke("✅ Recycle Bin emptied.");
                    }
                    catch { }
                }
                else
                {
                    foreach (var path in target.PathPatterns)
                    {
                        if (string.IsNullOrWhiteSpace(path)) continue;

                        try
                        {
                            if (Directory.Exists(path))
                            {
                                var dirInfo = new DirectoryInfo(path);
                                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                                {
                                    if (cancellationToken.IsCancellationRequested) break;
                                    try
                                    {
                                        var len = file.Length;
                                        file.Delete();
                                        targetCleaned += len;
                                    }
                                    catch { } // Skip locked files safely
                                }

                                foreach (var subDir in dirInfo.EnumerateDirectories())
                                {
                                    if (cancellationToken.IsCancellationRequested) break;
                                    try
                                    {
                                        subDir.Delete(true);
                                    }
                                    catch { }
                                }
                            }
                            else if (File.Exists(path))
                            {
                                var fi = new FileInfo(path);
                                var len = fi.Length;
                                fi.Delete();
                                targetCleaned += len;
                            }
                        }
                        catch { }
                    }
                }

                cleanedBytes += targetCleaned;
                target.BytesFound = Math.Max(0, target.BytesFound - targetCleaned);
                target.Status = "Cleaned";
                target.IsBusy = false;
            }
        }, cancellationToken);

        onLog?.Invoke($"🎉 Cleaned a total of {FormatBytes(cleanedBytes)}!");
        return cleanedBytes;
    }

    private static long GetRecycleBinSize()
    {
        try
        {
            long size = 0;
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var d in drives)
            {
                var rbPath = Path.Combine(d.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(rbPath))
                {
                    var dir = new DirectoryInfo(rbPath);
                    foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        try { size += file.Length; } catch { }
                    }
                }
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{(double)bytes / (1024 * 1024 * 1024):F2} GB",
        >= 1024 * 1024 => $"{(double)bytes / (1024 * 1024):F1} MB",
        >= 1024 => $"{(double)bytes / 1024:F0} KB",
        > 0 => $"{bytes} B",
        _ => "0 B"
    };
}
