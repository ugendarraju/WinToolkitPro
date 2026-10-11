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

            if (IsSuccessExitCode(process.ExitCode))
            {
                app.Status = InstallStatus.Installed;
                app.StatusMessage = process.ExitCode == 3010 ? "Installed (Reboot Required)" : "Installed successfully";
                app.Progress = 100;
                return true;
            }
            else
            {
                app.Status = InstallStatus.Failed;
                app.StatusMessage = $"Failed ({GetExitCodeExplanation(process.ExitCode)})";
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

        if (separatorIndex > 0 && separatorIndex + 1 < outputLines.Count)
        {
            var headerLine = outputLines[separatorIndex - 1];
            int idCol = headerLine.IndexOf("Id", StringComparison.Ordinal);
            int verCol = headerLine.IndexOf("Version", StringComparison.Ordinal);
            int availCol = headerLine.IndexOf("Available", StringComparison.Ordinal);
            int srcCol = headerLine.IndexOf("Source", StringComparison.Ordinal);

            for (int i = separatorIndex + 1; i < outputLines.Count; i++)
            {
                var line = outputLines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains("upgrades available") || line.Contains("package(s) have version")) continue;

                string name, id, installedVer, availableVer, source = "winget";

                if (idCol != -1 && verCol != -1 && availCol != -1 && line.Length >= verCol)
                {
                    name = (line.Length > idCol ? line.Substring(0, idCol) : line).Trim();
                    id = (line.Length >= verCol ? line.Substring(idCol, verCol - idCol) : line.Substring(idCol)).Trim();
                    installedVer = (line.Length >= availCol ? line.Substring(verCol, availCol - verCol) : line.Substring(verCol)).Trim();

                    if (srcCol != -1 && line.Length >= srcCol)
                    {
                        availableVer = line.Substring(availCol, srcCol - availCol).Trim();
                        source = line.Substring(srcCol).Trim();
                    }
                    else if (line.Length > availCol)
                    {
                        availableVer = line.Substring(availCol).Trim();
                    }
                    else
                    {
                        availableVer = string.Empty;
                    }
                }
                else
                {
                    var parts = Regex.Split(line.Trim(), @"\s{2,}");
                    if (parts.Length < 4) continue;
                    name = parts[0].Trim();
                    id = parts[1].Trim();
                    installedVer = parts[2].Trim();
                    availableVer = parts[3].Trim();
                    if (parts.Length > 4) source = parts[4].Trim();
                }

                // Clean any stray tokens or whitespace from Package ID
                if (id.Contains(' '))
                {
                    id = id.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                }

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                {
                    list.Add(new AppUpgradeItem
                    {
                        Name = name,
                        PackageId = id,
                        InstalledVersion = installedVer,
                        AvailableVersion = availableVer,
                        Source = string.IsNullOrWhiteSpace(source) ? "winget" : source
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
    /// Checks whether an exit code represents a success, benign condition, or reboot-required status.
    /// </summary>
    public static bool IsSuccessExitCode(int exitCode)
    {
        return exitCode == 0 ||
               exitCode == 3010 ||          // ERROR_SUCCESS_REBOOT_REQUIRED
               exitCode == 1641 ||          // ERROR_SUCCESS_REBOOT_INITIATED
               exitCode == 2359302 ||       // WU_S_ALREADY_INSTALLED
               exitCode == -1978335189 ||   // WINGET_INSTALLED_STATUS_ARP_ENTRY_NOT_FOUND (0x8A15002B)
               exitCode == -1978335175 ||   // WINGET_INSTALLED_STATUS_UPDATE_NOT_APPLICABLE (0x8A150039)
               exitCode == -1978335188 ||   // WINGET_INSTALLED_STATUS_ALREADY_INSTALLED (0x8A15002C)
               exitCode == -1978335215;     // WINGET_INSTALLED_STATUS_INSTALL_NOT_APPLICABLE
    }

    private static readonly Dictionary<string, string[]> KnownProcessMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "OBSProject.OBSStudio", new[] { "obs64", "obs32", "obs" } },
        { "Google.Chrome", new[] { "chrome" } },
        { "Opera.Opera", new[] { "opera" } },
        { "Mozilla.Firefox", new[] { "firefox" } },
        { "Zoom.Zoom.EXE", new[] { "Zoom" } },
        { "Microsoft.Teams", new[] { "ms-teams", "Teams" } },
        { "Microsoft.VisualStudioCode", new[] { "Code" } },
        { "OpenJS.NodeJS", new[] { "node" } },
        { "CodecGuide.K-LiteCodecPack.Full", new[] { "mpc-hc64", "mpc-hc", "K-Lite_Codec_Pack" } },
        { "PuTTY.PuTTY", new[] { "putty", "pageant", "pscp", "psftp", "puttygen" } },
        { "Mega.MEGASync", new[] { "MEGAsync" } },
        { "Wondershare.Filmora", new[] { "Filmora" } }
    };

    private void TryCloseLockingProcesses(string packageId, string name, Action<string>? onLog)
    {
        if (!KnownProcessMap.TryGetValue(packageId, out var procNames))
        {
            var parts = packageId.Split('.');
            var candidate = parts.Length > 1 ? parts[^1] : packageId;
            procNames = new[] { candidate };
        }

        foreach (var pName in procNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(pName);
                foreach (var p in procs)
                {
                    try
                    {
                        onLog?.Invoke($"[{name}] Closing active application process {p.ProcessName} (PID {p.Id}) to release file locks...");
                        p.CloseMainWindow();
                        if (!p.WaitForExit(1500))
                        {
                            p.Kill();
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Translates numeric installer exit codes into descriptive, user-friendly messages.
    /// </summary>
    public static string GetExitCodeExplanation(int exitCode)
    {
        return exitCode switch
        {
            0 => "Success",
            3010 => "Success (Reboot Required)",
            1641 => "Success (Reboot Initiated)",
            2359302 => "Already Installed",
            -1978335189 => "Installed successfully",
            -1978335175 => "Already up to date",
            -1978335188 => "Already up to date",
            -1978334959 => "Application in use (Process active in background)",
            1603 => "Access Denied / UAC required (Install requires Administrator privileges)",
            1618 => "Another installer is currently busy (Wait a moment and retry)",
            -1978334967 => "Hash verification mismatch (Manifest checksum differs from CDN)",
            -1978335212 => "Package not found in configured sources",
            -1978335120 => "Package not found in configured source",
            _ => $"Exit code {exitCode}"
        };
    }

    /// <summary>
    /// Upgrades an individual application with automated self-healing retry.
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

        var sourceArg = !string.IsNullOrWhiteSpace(item.Source) && !item.Source.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? $"--source \"{item.Source}\""
            : "";

        // Phase 1: Silent upgrade with agreements, source isolation, dependencies skipped, and unknown versions enabled
        var args1 = $"upgrade --id \"{item.PackageId}\" --exact {sourceArg} --silent --accept-package-agreements --accept-source-agreements --include-unknown --include-pinned --skip-dependencies --disable-interactivity";
        onLog?.Invoke($"[{item.Name}] Executing silent upgrade: winget {args1}");

        var (exitCode1, lastOutput1) = await RunWingetUpgradeProcessAsync(args1, item, onLog, cancellationToken);

        if (IsSuccessExitCode(exitCode1))
        {
            item.IsBusy = false;
            item.Progress = 100;
            item.IsSuccess = true;
            item.IsFailed = false;
            item.StatusMessage = exitCode1 == 3010 ? "Updated (Reboot Required)" : "Updated to " + item.AvailableVersion;
            onLog?.Invoke($"[{item.Name}] ✅ {item.StatusMessage}");
            return true;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            item.IsBusy = false;
            item.StatusMessage = "Cancelled";
            return false;
        }

        // If locked by running application, close it automatically before retry
        if (exitCode1 == -1978334959 || lastOutput1.Contains("in use by application", StringComparison.OrdinalIgnoreCase))
        {
            onLog?.Invoke($"[{item.Name}] Detected file lock. Terminating active background application process...");
            TryCloseLockingProcesses(item.PackageId, item.Name, onLog);
            await Task.Delay(1000, cancellationToken);
        }

        // Phase 2: Self-healing retry with --force (bypasses hash mismatches, pinned locks, and strict silent failures)
        var reason1 = GetExitCodeExplanation(exitCode1);
        onLog?.Invoke($"[{item.Name}] Notice: Silent attempt exited ({reason1}). Executing self-healing retry with --force...");
        item.StatusMessage = "Retrying with --force...";
        item.Progress = 25;

        var args2 = $"upgrade --id \"{item.PackageId}\" --exact {sourceArg} --force --include-unknown --include-pinned --skip-dependencies --accept-package-agreements --accept-source-agreements";
        var (exitCode2, lastOutput2) = await RunWingetUpgradeProcessAsync(args2, item, onLog, cancellationToken);

        item.IsBusy = false;
        item.Progress = 100;

        if (IsSuccessExitCode(exitCode2))
        {
            item.IsSuccess = true;
            item.IsFailed = false;
            item.StatusMessage = exitCode2 == 3010 ? "Updated (Reboot Required)" : "Updated to " + item.AvailableVersion;
            onLog?.Invoke($"[{item.Name}] ✅ {item.StatusMessage} (after self-healing retry)");
            return true;
        }

        // If still failed, explain the exact root cause
        var finalExplanation = GetExitCodeExplanation(exitCode2 != 0 ? exitCode2 : exitCode1);
        item.IsSuccess = false;
        item.IsFailed = true;
        item.ErrorMessage = finalExplanation;
        item.StatusMessage = $"Failed ({finalExplanation})";
        onLog?.Invoke($"[{item.Name}] ❌ Upgrade failed: {finalExplanation}. Detail: {(string.IsNullOrWhiteSpace(lastOutput2) ? lastOutput1 : lastOutput2)}");
        return false;
    }

    private async Task<(int ExitCode, string LastOutput)> RunWingetUpgradeProcessAsync(
        string arguments,
        AppUpgradeItem item,
        Action<string>? onLog,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var lastOutput = string.Empty;

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lastOutput = e.Data;
            onLog?.Invoke($"[{item.Name}] {e.Data}");

            var match = ProgressRegex.Match(e.Data);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
            {
                item.Progress = percent;
                item.StatusMessage = percent >= 100 ? "Applying upgrade..." : $"Downloading: {percent}%";
            }
            else if (e.Data.Contains("Installing", StringComparison.OrdinalIgnoreCase) ||
                     e.Data.Contains("Starting package install", StringComparison.OrdinalIgnoreCase))
            {
                item.StatusMessage = "Applying upgrade...";
                item.Progress = 85;
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                lastOutput = e.Data;
                onLog?.Invoke($"[{item.Name}] [stderr] {e.Data}");
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

            return (process.ExitCode, lastOutput);
        }
        catch (OperationCanceledException)
        {
            return (-1, "Operation cancelled");
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// Launches an elevated PowerShell window to run an interactive upgrade for stubborn or prompt-heavy packages.
    /// </summary>
    public void LaunchInteractiveUpgrade(AppUpgradeItem item)
    {
        var sourceArg = !string.IsNullOrWhiteSpace(item.Source) && !item.Source.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            ? $"--source \"{item.Source}\""
            : "";

        var arguments = $"upgrade --id \"{item.PackageId}\" {sourceArg} --force --include-unknown --accept-package-agreements --accept-source-agreements";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Write-Host 'Upgrading {item.Name}...' -ForegroundColor Cyan; winget {arguments}; Write-Host ''; Write-Host 'Done. Press any key to exit.' -ForegroundColor Green; $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        Process.Start(psi);
    }
}
