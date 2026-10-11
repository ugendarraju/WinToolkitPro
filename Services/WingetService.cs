using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinToolkit.Models;

namespace WinToolkit.Services;

public partial class WingetService
{
    private static readonly Regex ProgressRegex = new(@"(\d{1,3})%", RegexOptions.Compiled);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    static WingetService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WinToolkit/1.0 (Windows NT 10.0; Win64; x64)");
    }

    public async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(startInfo);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> InstallAppAsync(
        AppItem app,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        if (app.InstallerType.Equals("zip_bat", StringComparison.OrdinalIgnoreCase))
        {
            return await InstallZipBatAppAsync(app, onLog, cancellationToken);
        }

        return await InstallWingetAppAsync(app, onLog, cancellationToken);
    }

    private async Task<bool> InstallWingetAppAsync(
        AppItem app,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        app.Status = InstallStatus.Downloading;
        app.StatusMessage = "Starting download...";
        app.Progress = 5;

        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = $"install --id \"{app.WingetId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            onLog?.Invoke($"[{app.Name}] {e.Data}");

            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
            {
                app.Progress = percent;
                if (percent >= 100)
                {
                    app.Status = InstallStatus.Installing;
                    app.StatusMessage = "Installing package silently...";
                }
                else
                {
                    app.StatusMessage = $"Downloading: {percent}%";
                }
            }
            else if (e.Data.Contains("Installing", StringComparison.OrdinalIgnoreCase) ||
                     e.Data.Contains("Starting package install", StringComparison.OrdinalIgnoreCase))
            {
                app.Status = InstallStatus.Installing;
                app.StatusMessage = "Installing package silently...";
                app.Progress = 90;
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                onLog?.Invoke($"[{app.Name}] ERROR: {e.Data}");
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            if (process.ExitCode == 0 || process.ExitCode == -1978335189 || process.ExitCode == -1978335175)
            {
                app.Status = InstallStatus.Installed;
                app.StatusMessage = "Installed successfully";
                app.Progress = 100;
                return true;
            }
            else
            {
                app.Status = InstallStatus.Failed;
                app.StatusMessage = $"Failed (Exit code: {process.ExitCode})";
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            app.Status = InstallStatus.Idle;
            app.StatusMessage = "Cancelled";
            app.Progress = 0;
            return false;
        }
        catch (Exception ex)
        {
            app.Status = InstallStatus.Failed;
            app.StatusMessage = $"Error: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> InstallZipBatAppAsync(
        AppItem app,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            app.Status = InstallStatus.Downloading;
            app.StatusMessage = "Locating download package...";
            app.Progress = 5;

            var downloadUrl = app.DownloadUrl;

            // If it's the VC++ AIO package, resolve the latest version dynamically
            if (app.Id == "vcredist_aio" || string.IsNullOrWhiteSpace(downloadUrl))
            {
                try
                {
                    onLog?.Invoke($"[{app.Name}] Resolving latest release of VC++ All-in-One...");
                    var releaseJson = await HttpClient.GetStringAsync("https://api.github.com/repos/abbodi1406/vcredist/releases/latest", cancellationToken);
                    using var doc = JsonDocument.Parse(releaseJson);
                    if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                                onLog?.Invoke($"[{app.Name}] Found latest version: {name}");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"[{app.Name}] Warning checking GitHub releases: {ex.Message}. Falling back to default URL or winget.");
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                // Fallback to winget abbodi1406.vcredist
                onLog?.Invoke($"[{app.Name}] Using winget abbodi1406.vcredist fallback...");
                app.WingetId = "abbodi1406.vcredist";
                return await InstallWingetAppAsync(app, onLog, cancellationToken);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "WinToolkit", app.Id);
            try
            {
                Directory.CreateDirectory(tempDir);
                var zipPath = Path.Combine(tempDir, "package.zip");
                var extractPath = Path.Combine(tempDir, "extracted");

                onLog?.Invoke($"[{app.Name}] Downloading from {downloadUrl} to OS Temp directory...");
                using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    await using var streamToReadFrom = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using var streamToWriteTo = File.Create(zipPath);

                    var buffer = new byte[81920];
                    var bytesRead = 0;
                    var totalBytesRead = 0L;

                    while ((bytesRead = await streamToReadFrom.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await streamToWriteTo.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        totalBytesRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var pct = (double)totalBytesRead / totalBytes * 50.0;
                            app.Progress = Math.Min(50.0, pct);
                            app.StatusMessage = $"Downloading: {(int)(pct * 2)}%";
                        }
                    }
                }

                app.Status = InstallStatus.Installing;
                app.StatusMessage = "Extracting files in OS Temp...";
                app.Progress = 60;
                onLog?.Invoke($"[{app.Name}] Extracting zip package to {extractPath}...");

                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(zipPath, extractPath, true);

                // Locate .bat file
                var batTarget = app.BatFile ?? "install_all.bat";
                var batFiles = Directory.GetFiles(extractPath, batTarget, SearchOption.AllDirectories);
                if (batFiles.Length == 0)
                {
                    batFiles = Directory.GetFiles(extractPath, "*.bat", SearchOption.AllDirectories);
                }

                if (batFiles.Length == 0)
                {
                    throw new FileNotFoundException($"Could not find batch installation script ({batTarget}) in package.");
                }

                var targetBat = batFiles[0];
                var batWorkingDir = Path.GetDirectoryName(targetBat) ?? extractPath;
                var batArgs = app.BatArguments ?? "-ai";

                app.StatusMessage = "Running unattended batch installation...";
                app.Progress = 75;
                onLog?.Invoke($"[{app.Name}] Running script: {Path.GetFileName(targetBat)} {batArgs}...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{targetBat}\" {batArgs}\"",
                    WorkingDirectory = batWorkingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        onLog?.Invoke($"[{app.Name}] {e.Data}");
                    }
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        onLog?.Invoke($"[{app.Name}] ERROR: {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited) process.Kill(true);
                    }
                    catch { }
                }))
                {
                    await process.WaitForExitAsync(cancellationToken);
                }

                app.Progress = 100;
                app.Status = InstallStatus.Installed;
                app.StatusMessage = "All runtimes installed successfully";
                onLog?.Invoke($"[{app.Name}] Installation finished successfully!");
                return true;
            }
            finally
            {
                // Clean up OS temp folder for this package
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                        onLog?.Invoke($"[{app.Name}] Cleaned up temporary installation folder: {tempDir}");
                    }
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"[{app.Name}] Note: Could not immediately delete temp folder (will be cleaned on exit): {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            app.Status = InstallStatus.Idle;
            app.StatusMessage = "Cancelled";
            app.Progress = 0;
            return false;
        }
        catch (Exception ex)
        {
            app.Status = InstallStatus.Failed;
            app.StatusMessage = $"Failed: {ex.Message}";
            onLog?.Invoke($"[{app.Name}] Installation error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Cleans up all temporary downloads, extracted archives, and cached installer files from OS Temp.
    /// </summary>
    public async Task CleanTempFilesAsync(Action<string>? onLog = null)
    {
        await Task.Run(() =>
        {
            try
            {
                var baseTemp = Path.Combine(Path.GetTempPath(), "WinToolkit");
                if (Directory.Exists(baseTemp))
                {
                    onLog?.Invoke("🧹 Cleaning temporary downloads and cache from OS Temp...");
                    Directory.Delete(baseTemp, true);
                    onLog?.Invoke("✅ OS Temp folder cleaned successfully.");
                }

                // Also clean loose temporary winget download caches
                var wingetTemp = Path.Combine(Path.GetTempPath(), "WinGet");
                if (Directory.Exists(wingetTemp))
                {
                    try
                    {
                        Directory.Delete(wingetTemp, true);
                        onLog?.Invoke("✅ WinGet temporary installer cache cleared.");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Notice during temp cleanup: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Scans installed applications for available updates using winget upgrade.
    /// </summary>
    public async Task<List<AppUpgradeItem>> ScanUpgradesAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Querying installed applications for updates...");

        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = "upgrade --include-unknown",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        var outputLines = new List<string>();

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                outputLines.Add(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var list = new List<AppUpgradeItem>();
        int separatorIndex = -1;

        for (int i = 0; i < outputLines.Count; i++)
        {
            var line = outputLines[i];
            if (line.StartsWith("---") || line.Contains("------"))
            {
                separatorIndex = i;
                break;
            }
        }

        if (separatorIndex != -1 && separatorIndex + 1 < outputLines.Count)
        {
            for (int i = separatorIndex + 1; i < outputLines.Count; i++)
            {
                var line = outputLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains("upgrades available") || line.Contains("package(s) have version")) continue;

                // Typical line format: Name (can have spaces) | Id | Version | Available | Source
                // Match multiple whitespace separators
                var parts = Regex.Split(line.Trim(), @"\s{2,}");
                if (parts.Length >= 4)
                {
                    var name = parts[0].Trim();
                    var id = parts[1].Trim();
                    var installedVer = parts[2].Trim();
                    var availableVer = parts[3].Trim();
                    var source = parts.Length > 4 ? parts[4].Trim() : "winget";

                    list.Add(new AppUpgradeItem
                    {
                        Name = name,
                        PackageId = id,
                        InstalledVersion = installedVer,
                        AvailableVersion = availableVer,
                        Source = source
                    });
                }
            }
        }

        onLog?.Invoke(list.Count > 0 
            ? $"Found {list.Count} installed application(s) with newer versions available."
            : "All installed applications are up to date!");

        return list;
    }

    /// <summary>
    /// Upgrades an individual application silently using winget upgrade.
    /// </summary>
    public async Task<bool> UpgradeAppAsync(
        AppUpgradeItem item,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        item.StatusMessage = "Starting upgrade...";
        item.IsBusy = true;
        item.IsSuccess = false;
        item.IsFailed = false;
        item.Progress = 10;

        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = $"upgrade --id \"{item.PackageId}\" --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            onLog?.Invoke($"[{item.Name}] {e.Data}");

            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
            {
                item.Progress = percent;
                if (percent >= 100)
                {
                    item.StatusMessage = "Applying upgrade...";
                }
                else
                {
                    item.StatusMessage = $"Downloading: {percent}%";
                }
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch { }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            var success = process.ExitCode == 0;
            item.IsBusy = false;
            item.Progress = 100;

            if (success)
            {
                item.StatusMessage = "Updated to " + item.AvailableVersion;
                item.IsSuccess = true;
                item.IsFailed = false;
                onLog?.Invoke($"[{item.Name}] Updated successfully!");
            }
            else
            {
                item.StatusMessage = "Upgrade Failed";
                item.IsSuccess = false;
                item.IsFailed = true;
                item.ErrorMessage = $"Exit code: {process.ExitCode}";
                onLog?.Invoke($"[{item.Name}] Upgrade ended with exit code {process.ExitCode}");
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            item.IsBusy = false;
            item.StatusMessage = "Cancelled";
            return false;
        }
        catch (Exception ex)
        {
            item.IsBusy = false;
            item.StatusMessage = "Error: " + ex.Message;
            item.IsFailed = true;
            item.ErrorMessage = ex.Message;
            onLog?.Invoke($"[{item.Name}] Error: {ex.Message}");
            return false;
        }
    }
}
