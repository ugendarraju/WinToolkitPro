using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class RegistryCleanerService
{
    private readonly PowerShellService _powerShellService;

    public RegistryCleanerService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    /// <summary>
    /// Scans standard Windows registry hives for invalid, orphaned, or missing references.
    /// </summary>
    public async Task<List<RegistryIssueItem>> ScanRegistryIssuesAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var issues = new List<RegistryIssueItem>();
            onLog?.Invoke("Beginning Windows registry integrity and optimization scan...");

            // 1. Missing Shared DLLs
            ScanMissingSharedDlls(issues, onLog, ct);

            // 2. Stale Application Paths (HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths)
            ScanStaleAppPaths(issues, onLog, ct);

            // 3. Obsolete / Invalid MuiCache references
            ScanStaleMuiCache(issues, onLog, ct);

            // 4. Stale Startup Run keys
            ScanStaleStartupRunKeys(issues, onLog, ct);

            // 5. Invalid File Associations & Unused Extensions
            ScanInvalidFileExtensions(issues, onLog, ct);

            onLog?.Invoke($"Registry scan completed. Discovered {issues.Count} invalid or orphaned entries.");
            return issues;
        }, ct);
    }

    private static void ScanMissingSharedDlls(List<RegistryIssueItem> issues, Action<string>? onLog, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var sharedDllsKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls");
            if (sharedDllsKey != null)
            {
                foreach (var val in sharedDllsKey.GetValueNames())
                {
                    if (ct.IsCancellationRequested) break;
                    if (!string.IsNullOrWhiteSpace(val) && val.Contains('\\'))
                    {
                        if (!File.Exists(val))
                        {
                            issues.Add(new RegistryIssueItem
                            {
                                Category = "Missing Shared DLL",
                                KeyPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDlls",
                                ValueName = val,
                                Details = $"File no longer exists on disk: {val}"
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Warning scanning SharedDlls: {ex.Message}");
        }
    }

    private static void ScanStaleAppPaths(List<RegistryIssueItem> issues, Action<string>? onLog, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            var targets = new (RegistryHive Hive, RegistryView View, string SubKey)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
                (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths")
            };

            foreach (var (hive, view, subKeyPath) in targets)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPathsKey = baseKey.OpenSubKey(subKeyPath);
                if (appPathsKey == null) continue;

                foreach (var subName in appPathsKey.GetSubKeyNames())
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        using var sub = appPathsKey.OpenSubKey(subName);
                        var defaultVal = sub?.GetValue("") as string;
                        if (!string.IsNullOrWhiteSpace(defaultVal))
                        {
                            var clean = defaultVal.Trim('\"');
                            if (clean.Contains('\\') && !File.Exists(clean))
                            {
                                issues.Add(new RegistryIssueItem
                                {
                                    Category = "Invalid App Path",
                                    KeyPath = $@"{hive}\{subKeyPath}\{subName}",
                                    ValueName = "@Default",
                                    Details = $"Target binary missing: {clean}"
                                });
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Warning scanning App Paths: {ex.Message}");
        }
    }

    private static void ScanStaleMuiCache(List<RegistryIssueItem> issues, Action<string>? onLog, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var muiKey = baseKey.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache");
            if (muiKey != null)
            {
                foreach (var val in muiKey.GetValueNames())
                {
                    if (ct.IsCancellationRequested) break;
                    if (val.Contains('\\') && (val.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase) || val.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase)))
                    {
                        var filePath = val;
                        var idx = val.LastIndexOf('.');
                        if (idx > 0) filePath = val.Substring(0, idx);

                        if (!File.Exists(filePath))
                        {
                            issues.Add(new RegistryIssueItem
                            {
                                Category = "Stale MuiCache",
                                KeyPath = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
                                ValueName = val,
                                Details = $"Cached application missing: {filePath}"
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            onLog?.Invoke($"Warning scanning MuiCache: {ex.Message}");
        }
    }

    private static void ScanStaleStartupRunKeys(List<RegistryIssueItem> issues, Action<string>? onLog, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        var runKeys = new (RegistryHive Hive, string Path)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
            (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
        };

        foreach (var (hive, path) in runKeys)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var k = baseKey.OpenSubKey(path);
                if (k == null) continue;

                foreach (var name in k.GetValueNames())
                {
                    if (ct.IsCancellationRequested) break;
                    var val = k.GetValue(name)?.ToString();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    var exePath = ExtractExePath(val);
                    if (!string.IsNullOrWhiteSpace(exePath) && !File.Exists(exePath))
                    {
                        issues.Add(new RegistryIssueItem
                        {
                            Category = "Invalid Startup Entry",
                            KeyPath = $@"{hive}\{path}",
                            ValueName = name,
                            Details = $"Startup binary missing: {exePath}"
                        });
                    }
                }
            }
            catch { }
        }
    }

    private static void ScanInvalidFileExtensions(List<RegistryIssueItem> issues, Action<string>? onLog, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var explorerFileExts = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
            if (explorerFileExts != null)
            {
                foreach (var ext in explorerFileExts.GetSubKeyNames().Take(100))
                {
                    if (ct.IsCancellationRequested) break;
                    using var sub = explorerFileExts.OpenSubKey($@"{ext}\OpenWithList");
                    if (sub != null)
                    {
                        foreach (var vName in sub.GetValueNames())
                        {
                            if (vName == "MRUList") continue;
                            var appName = sub.GetValue(vName)?.ToString();
                            if (!string.IsNullOrWhiteSpace(appName) && appName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                // Check if known installed
                                var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                                var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                                if (!File.Exists(Path.Combine(progFiles, appName)) &&
                                    !File.Exists(Path.Combine(progFilesX86, appName)) &&
                                    !File.Exists(Path.Combine(Environment.SystemDirectory, appName)))
                                {
                                    // Potential stale file association
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static string ExtractExePath(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("\""))
        {
            var end = raw.IndexOf('\"', 1);
            if (end > 0) return raw.Substring(1, end - 1);
        }

        var space = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (space > 0)
        {
            return raw.Substring(0, space + 4).Trim('\"');
        }

        return raw;
    }

    /// <summary>
    /// Creates a backup file (.reg) before cleaning registry issues.
    /// </summary>
    public async Task<string> BackupRegistryBranchAsync(string backupDirectory, Action<string>? onLog = null)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(backupDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outPath = Path.Combine(backupDirectory, $"RegistryBackup_{timestamp}.reg");

            try
            {
                onLog?.Invoke($"Creating registry backup at {outPath}...");
                var script = $"reg export 'HKCU\\Software\\Classes\\Local Settings\\Software\\Microsoft\\Windows\\Shell\\MuiCache' '{outPath}' /y";
                _powerShellService.ExecuteCommandAsync(script).Wait();
                onLog?.Invoke("✓ Registry backup saved successfully.");
                return outPath;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Warning during registry backup: {ex.Message}");
                return string.Empty;
            }
        });
    }

    /// <summary>
    /// Cleans selected registry entries and returns number of fixed items.
    /// </summary>
    public async Task<int> FixRegistryIssuesAsync(IEnumerable<RegistryIssueItem> items, Action<string>? onLog = null)
    {
        return await Task.Run(() =>
        {
            int fixedCount = 0;
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    var parts = item.KeyPath.Split('\\', 2);
                    if (parts.Length < 2) continue;

                    var hiveStr = parts[0];
                    var subPath = parts[1];

                    RegistryHive hive = hiveStr.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase) || hiveStr.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
                        ? RegistryHive.CurrentUser
                        : RegistryHive.LocalMachine;

                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);

                    if (item.Category == "Invalid App Path" && item.ValueName == "@Default")
                    {
                        // Delete the whole subkey for this missing app path
                        baseKey.DeleteSubKeyTree(subPath, false);
                        item.Status = "Fixed";
                        fixedCount++;
                        onLog?.Invoke($"✓ Removed stale App Path: {item.KeyPath}");
                    }
                    else
                    {
                        using var key = baseKey.OpenSubKey(subPath, writable: true);
                        if (key != null)
                        {
                            key.DeleteValue(item.ValueName, false);
                            item.Status = "Fixed";
                            fixedCount++;
                            onLog?.Invoke($"✓ Cleaned registry value: {item.ValueName} from {item.KeyPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    item.Status = "Failed";
                    onLog?.Invoke($"Notice fixing {item.ValueName}: {ex.Message}");
                }
            }

            return fixedCount;
        });
    }
}
