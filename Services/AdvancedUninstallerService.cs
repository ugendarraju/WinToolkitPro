using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class AdvancedUninstallerService
{
    private readonly PowerShellService _powerShellService;

    public AdvancedUninstallerService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    /// <summary>
    /// Scans 64-bit, 32-bit (Wow6432Node), and CurrentUser registry hives for installed desktop programs.
    /// Fast direct Win32 registry scan.
    /// </summary>
    public async Task<List<InstalledSoftwareItem>> ScanInstalledSoftwareAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            onLog?.Invoke("Scanning system registry for installed software packages...");
            var results = new Dictionary<string, InstalledSoftwareItem>(StringComparer.OrdinalIgnoreCase);

            var targets = new (RegistryHive Hive, RegistryView View, string SubKey, string Arch)[]
            {
                (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "x64"),
                (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "x86"),
                (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "User")
            };

            foreach (var (hive, view, subKeyPath, arch) in targets)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                    if (uninstallKey == null) continue;

                    foreach (var subName in uninstallKey.GetSubKeyNames())
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            using var appKey = uninstallKey.OpenSubKey(subName);
                            if (appKey == null) continue;

                            var displayName = appKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Filter out system updates and patches if marked SystemComponent
                            var systemComponent = appKey.GetValue("SystemComponent");
                            var isSys = systemComponent is int sc && sc == 1;

                            var parentKey = appKey.GetValue("ParentKeyName") as string;
                            if (!string.IsNullOrWhiteSpace(parentKey)) continue; // Child update or patch

                            var uninstallString = appKey.GetValue("UninstallString") as string ?? string.Empty;
                            var quietUninstall = appKey.GetValue("QuietUninstallString") as string ?? string.Empty;
                            var installLocation = appKey.GetValue("InstallLocation") as string ?? string.Empty;
                            var publisher = appKey.GetValue("Publisher") as string ?? string.Empty;
                            var displayVersion = appKey.GetValue("DisplayVersion") as string ?? string.Empty;
                            var installDate = appKey.GetValue("InstallDate") as string ?? string.Empty;
                            var displayIcon = appKey.GetValue("DisplayIcon") as string ?? string.Empty;

                            long estimatedSizeKb = 0;
                            var sizeVal = appKey.GetValue("EstimatedSize");
                            if (sizeVal is int sInt) estimatedSizeKb = sInt;
                            else if (sizeVal is long sLong) estimatedSizeKb = sLong;

                            // If install location exists, get folder size estimate if not available
                            if (estimatedSizeKb <= 0 && !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                            {
                                try
                                {
                                    // Quick estimate top level
                                    var dirInfo = new DirectoryInfo(installLocation);
                                    estimatedSizeKb = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Sum(f => f.Length) / 1024;
                                }
                                catch { }
                            }

                            var item = new InstalledSoftwareItem
                            {
                                DisplayName = displayName.Trim(),
                                DisplayVersion = displayVersion.Trim(),
                                Publisher = publisher.Trim(),
                                InstallLocation = installLocation.Trim(),
                                UninstallString = uninstallString.Trim(),
                                QuietUninstallString = quietUninstall.Trim(),
                                InstallDate = FormatInstallDate(installDate),
                                EstimatedSizeKb = estimatedSizeKb,
                                RegistryKeyPath = $@"{hive}\{subKeyPath}\{subName}",
                                Architecture = arch,
                                DisplayIcon = displayIcon.Trim(),
                                IsSystemComponent = isSys
                            };

                            // Deduplicate by name if identical, prioritizing x64 over x86
                            var keyId = $"{item.DisplayName}_{item.DisplayVersion}";
                            if (!results.ContainsKey(keyId))
                            {
                                results[keyId] = item;
                            }
                            else if (arch == "x64" && results[keyId].Architecture == "x86")
                            {
                                results[keyId] = item;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Warning reading registry {hive}: {ex.Message}");
                }
            }

            var list = results.Values.OrderBy(x => x.DisplayName).ToList();
            onLog?.Invoke($"Successfully indexed {list.Count} installed software programs.");
            return list;
        }, ct);
    }

    private static string FormatInstallDate(string rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return "--";
        if (rawDate.Length == 8 && int.TryParse(rawDate, out _))
        {
            // YYYYMMDD
            var y = rawDate.Substring(0, 4);
            var m = rawDate.Substring(4, 2);
            var d = rawDate.Substring(6, 2);
            return $"{y}-{m}-{d}";
        }
        return rawDate;
    }

    /// <summary>
    /// Scans for leftovers (registry entries and file system directories) for a given program.
    /// Scan modes: 0 = Safe, 1 = Moderate, 2 = Advanced.
    /// </summary>
    public async Task<List<UninstallLeftoverItem>> ScanLeftoversAsync(string appName, string installLocation, string publisher, int scanLevel, Action<string>? onLog = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var leftovers = new List<UninstallLeftoverItem>();
            onLog?.Invoke($"Scanning for residual traces of '{appName}' (Level: {(scanLevel == 0 ? "Safe" : scanLevel == 1 ? "Moderate" : "Advanced")})...");

            var safeTokens = CleanNameTokens(appName);
            if (safeTokens.Count == 0) return leftovers;

            var primaryToken = safeTokens[0];

            // 1. Check Install Location directory remnants
            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
            {
                try
                {
                    var dir = new DirectoryInfo(installLocation);
                    var size = GetDirectorySize(dir);
                    leftovers.Add(new UninstallLeftoverItem
                    {
                        Type = LeftoverType.Folder,
                        Path = installLocation,
                        Details = "Program installation directory",
                        SizeBytes = size
                    });
                }
                catch { }
            }

            // 2. Scan Common AppData and LocalAppData folders
            var searchPaths = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // Roaming
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) // Local
            };

            foreach (var basePath in searchPaths)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    var matches = Directory.GetDirectories(basePath)
                        .Where(d =>
                        {
                            var dirName = Path.GetFileName(d);
                            return safeTokens.Any(t => dirName.Contains(t, StringComparison.OrdinalIgnoreCase));
                        });

                    foreach (var m in matches)
                    {
                        // Ensure we never delete parent root directories
                        if (IsProtectedPath(m)) continue;

                        var size = GetDirectorySize(new DirectoryInfo(m));
                        leftovers.Add(new UninstallLeftoverItem
                        {
                            Type = LeftoverType.Folder,
                            Path = m,
                            Details = $"Leftover application data in {Path.GetFileName(basePath)}",
                            SizeBytes = size
                        });
                    }
                }
                catch { }
            }

            // 3. Scan Registry Software trees (HKCU\Software and HKLM\Software)
            var regHives = new[]
            {
                (RegistryHive.CurrentUser, @"Software"),
                (RegistryHive.LocalMachine, @"Software"),
                (RegistryHive.LocalMachine, @"Software\WOW6432Node")
            };

            foreach (var (hive, root) in regHives)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                    using var rootKey = baseKey.OpenSubKey(root);
                    if (rootKey == null) continue;

                    foreach (var keyName in rootKey.GetSubKeyNames())
                    {
                        if (ct.IsCancellationRequested) break;

                        if (safeTokens.Any(t => keyName.Equals(t, StringComparison.OrdinalIgnoreCase) || keyName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (IsProtectedRegistryKey(keyName)) continue;

                            leftovers.Add(new UninstallLeftoverItem
                            {
                                Type = LeftoverType.RegistryKey,
                                Path = $@"{hive}\{root}\{keyName}",
                                Details = $"Residual Software registry branch"
                            });
                        }
                    }
                }
                catch { }
            }

            onLog?.Invoke($"Leftover analysis complete. Discovered {leftovers.Count} residual item(s).");
            return leftovers;
        }, ct);
    }

    private static List<string> CleanNameTokens(string name)
    {
        // Strip version numbers, brackets, etc.
        var tokens = name.Split(new[] { ' ', '-', '_', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !w.All(char.IsDigit) && !w.Equals("x64", StringComparison.OrdinalIgnoreCase) && !w.Equals("x86", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return tokens;
    }

    private static bool IsProtectedPath(string path)
    {
        var norm = path.TrimEnd('\\').ToLowerInvariant();
        var protectedDirs = new[]
        {
            @"c:\windows",
            @"c:\program files",
            @"c:\program files (x86)",
            @"c:\programdata",
            @"c:\users",
            Environment.GetFolderPath(Environment.SpecialFolder.System).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).ToLowerInvariant(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToLowerInvariant()
        };
        return protectedDirs.Contains(norm);
    }

    private static bool IsProtectedRegistryKey(string keyName)
    {
        var lower = keyName.ToLowerInvariant();
        var protectedKeys = new[] { "microsoft", "windows", "classes", "policies", "system", "clients", "registeredapplications", "vmware", "intel", "amd" };
        return protectedKeys.Contains(lower);
    }

    private static long GetDirectorySize(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Purges selected leftover items (deleting files, folders, and registry keys).
    /// </summary>
    public async Task<int> PurgeLeftoversAsync(IEnumerable<UninstallLeftoverItem> items, Action<string>? onLog = null)
    {
        return await Task.Run(() =>
        {
            int deleted = 0;
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    if (item.Type == LeftoverType.Folder && Directory.Exists(item.Path))
                    {
                        Directory.Delete(item.Path, true);
                        onLog?.Invoke($"✓ Removed folder: {item.Path}");
                        deleted++;
                    }
                    else if (item.Type == LeftoverType.File && File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                        onLog?.Invoke($"✓ Removed file: {item.Path}");
                        deleted++;
                    }
                    else if (item.Type == LeftoverType.RegistryKey)
                    {
                        DeleteRegistryKeyBranch(item.Path);
                        onLog?.Invoke($"✓ Purged registry key: {item.Path}");
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    onLog?.Invoke($"Notice deleting {item.Path}: {ex.Message}");
                }
            }
            return deleted;
        });
    }

    private static void DeleteRegistryKeyBranch(string fullKeyPath)
    {
        var parts = fullKeyPath.Split('\\', 2);
        if (parts.Length < 2) return;

        var hiveStr = parts[0];
        var subPath = parts[1];

        RegistryHive hive = hiveStr.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase) || hiveStr.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)
            ? RegistryHive.CurrentUser
            : RegistryHive.LocalMachine;

        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
        baseKey.DeleteSubKeyTree(subPath, false);
    }

    /// <summary>
    /// Executes the standard uninstaller command string.
    /// Supports quiet/silent flags if available.
    /// </summary>
    public async Task<bool> RunUninstallerAsync(InstalledSoftwareItem item, bool silent = false, Action<string>? onLog = null)
    {
        return await Task.Run(() =>
        {
            var cmd = silent && !string.IsNullOrWhiteSpace(item.QuietUninstallString)
                ? item.QuietUninstallString
                : item.UninstallString;

            if (string.IsNullOrWhiteSpace(cmd))
            {
                onLog?.Invoke($"No uninstall string registered for '{item.DisplayName}'.");
                return false;
            }

            onLog?.Invoke($"Launching uninstaller for {item.DisplayName}...");

            try
            {
                // Parse executable and arguments
                string fileName;
                string args = "";

                cmd = cmd.Trim();
                if (cmd.StartsWith("\""))
                {
                    var closeQuote = cmd.IndexOf('\"', 1);
                    if (closeQuote > 0)
                    {
                        fileName = cmd.Substring(1, closeQuote - 1);
                        args = cmd.Substring(closeQuote + 1).Trim();
                    }
                    else
                    {
                        fileName = cmd.Trim('\"');
                    }
                }
                else
                {
                    var spaceIdx = cmd.IndexOf(' ');
                    if (spaceIdx > 0)
                    {
                        fileName = cmd.Substring(0, spaceIdx);
                        args = cmd.Substring(spaceIdx + 1);
                    }
                    else
                    {
                        fileName = cmd;
                    }
                }

                // Append silent argument if requested and not present
                if (silent && !args.Contains("/q", StringComparison.OrdinalIgnoreCase) && !args.Contains("/s", StringComparison.OrdinalIgnoreCase))
                {
                    if (fileName.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
                    {
                        args += " /qn /norestart";
                    }
                    else
                    {
                        args += " /S";
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                    onLog?.Invoke($"Uninstaller process exited with code {proc.ExitCode}.");
                    return proc.ExitCode == 0 || proc.ExitCode == 3010; // 3010 is reboot required
                }
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Error executing uninstaller: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Creates a system restore point prior to uninstallation.
    /// </summary>
    public async Task<bool> CreateSystemRestorePointAsync(string description, Action<string>? onLog = null)
    {
        onLog?.Invoke($"Creating System Restore Point ('{description}')...");
        var script = $@"
            try {{
                Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description '{description}' -RestorePointType 'APPLICATION_UNINSTALL' -ErrorAction Stop
                Write-Output 'SUCCESS'
            }} catch {{
                Write-Output ('NOTICE: ' + $_.Exception.Message)
            }}
        ";

        var res = await _powerShellService.ExecuteCommandAsync(script);
        if (res.Output.Contains("SUCCESS"))
        {
            onLog?.Invoke("✓ System Restore Point created successfully.");
            return true;
        }
        onLog?.Invoke($"System restore point notice: {res.Output}");
        return false;
    }

    /// <summary>
    /// Scans installed browser extensions for Google Chrome, Edge, and Brave.
    /// </summary>
    public async Task<List<BrowserExtensionItem>> ScanBrowserExtensionsAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<BrowserExtensionItem>();
            onLog?.Invoke("Scanning web browsers for installed extensions and add-ons...");

            var browserLocations = new (string Browser, string Path)[]
            {
                ("Google Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data")),
                ("Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data")),
                ("Brave Browser", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BraveSoftware\Brave-Browser\User Data"))
            };

            foreach (var (browser, baseUserData) in browserLocations)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(baseUserData)) continue;

                try
                {
                    // Check profiles (Default, Profile 1, etc.)
                    var profileDirs = Directory.GetDirectories(baseUserData)
                        .Where(d => Path.GetFileName(d).Equals("Default", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(d).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));

                    foreach (var pDir in profileDirs)
                    {
                        var extDir = Path.Combine(pDir, "Extensions");
                        if (!Directory.Exists(extDir)) continue;

                        foreach (var sub in Directory.GetDirectories(extDir))
                        {
                            if (ct.IsCancellationRequested) break;
                            var extId = Path.GetFileName(sub);

                            // Inside is version directory (e.g. 1.0.0_0)
                            var versionDirs = Directory.GetDirectories(sub);
                            if (versionDirs.Length == 0) continue;

                            var vDir = versionDirs[0];
                            var manifestPath = Path.Combine(vDir, "manifest.json");
                            if (!File.Exists(manifestPath)) continue;

                            string extName = extId;
                            string version = Path.GetFileName(vDir);
                            string description = "Browser extension";

                            try
                            {
                                var json = File.ReadAllText(manifestPath);
                                using var doc = JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("name", out var nEl))
                                {
                                    var rawN = nEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(rawN) && !rawN.StartsWith("__MSG_"))
                                    {
                                        extName = rawN;
                                    }
                                }

                                if (doc.RootElement.TryGetProperty("version", out var vEl))
                                {
                                    version = vEl.GetString() ?? version;
                                }

                                if (doc.RootElement.TryGetProperty("description", out var dEl))
                                {
                                    var rawD = dEl.GetString();
                                    if (!string.IsNullOrWhiteSpace(rawD) && !rawD.StartsWith("__MSG_"))
                                    {
                                        description = rawD;
                                    }
                                }
                            }
                            catch { }

                            list.Add(new BrowserExtensionItem
                            {
                                Browser = browser,
                                Id = extId,
                                Name = extName,
                                Version = version,
                                Description = description,
                                InstallPath = sub
                            });
                        }
                    }
                }
                catch { }
            }

            onLog?.Invoke($"Found {list.Count} installed browser extensions.");
            return list;
        }, ct);
    }

    /// <summary>
    /// Removes a browser extension by deleting its extension directory.
    /// </summary>
    public async Task<bool> RemoveBrowserExtensionAsync(BrowserExtensionItem item, Action<string>? onLog = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(item.InstallPath))
                {
                    Directory.Delete(item.InstallPath, true);
                    onLog?.Invoke($"✓ Successfully uninstalled extension: {item.Name} ({item.Browser})");
                    item.Status = "Removed";
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Notice removing extension: {ex.Message}");
                return false;
            }
        });
    }
}
