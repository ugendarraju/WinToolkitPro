using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinToolkit.Services;

public class GitHubUpdateService
{
    private const string RepoOwner = "ugendarraju";
    private const string RepoName = "WinToolkitPro";
    private static readonly HttpClient HttpClient = new();

    static GitHubUpdateService()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WinToolkitPro-Updater", "1.5.0"));
        HttpClient.Timeout = TimeSpan.FromSeconds(8);
    }

    public record ReleaseUpdateInfo(
        bool UpdateAvailable,
        string CurrentVersion,
        string LatestVersion,
        string ReleaseTitle,
        string ReleaseNotes,
        string? DownloadUrl,
        string? AssetName,
        long AssetSize
    );

    public static string GetCurrentVersion()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.5.0";
    }

    public async Task<ReleaseUpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var currentVerStr = GetCurrentVersion();
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await HttpClient.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new ReleaseUpdateInfo(false, currentVerStr, currentVerStr, string.Empty, string.Empty, null, null, 0);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var releaseTitle = root.TryGetProperty("name", out var np) ? (np.GetString() ?? tagName) : tagName;
            var body = root.TryGetProperty("body", out var bp) ? (bp.GetString() ?? "") : "";

            // Normalize tag name (e.g. "v1.5.0" -> "1.5.0")
            var latestClean = tagName.TrimStart('v', 'V').Trim();
            var isNewer = IsVersionNewer(currentVerStr, latestClean);

            string? downloadUrl = null;
            string? assetName = null;
            long assetSize = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    // Prioritize .zip or .exe release assets
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        assetName = name;
                        assetSize = asset.TryGetProperty("size", out var sp) ? sp.GetInt64() : 0;
                        break;
                    }
                }
            }

            return new ReleaseUpdateInfo(
                isNewer,
                currentVerStr,
                latestClean,
                releaseTitle,
                body,
                downloadUrl,
                assetName,
                assetSize
            );
        }
        catch
        {
            return new ReleaseUpdateInfo(false, currentVerStr, currentVerStr, string.Empty, string.Empty, null, null, 0);
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(
        ReleaseUpdateInfo updateInfo,
        IProgress<double>? progress = null,
        Action<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(updateInfo.DownloadUrl)) return false;

        var tempDir = Path.Combine(Path.GetTempPath(), "WinToolkitPro_Update_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var downloadFile = Path.Combine(tempDir, updateInfo.AssetName ?? "update.zip");

        statusCallback?.Invoke($"Downloading v{updateInfo.LatestVersion} update payload...");

        // Stream download with progress
        using (var response = await HttpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? updateInfo.AssetSize;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(downloadFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[16384];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    progress?.Report(Math.Round((double)totalRead / totalBytes * 100.0, 1));
                }
            }
        }

        statusCallback?.Invoke("Preparing self-updating swap script...");

        var currentExePath = Process.GetCurrentProcess().MainModule?.FileName 
                             ?? Environment.ProcessPath 
                             ?? AppDomain.CurrentDomain.BaseDirectory;
        var currentAppDir = Path.GetDirectoryName(currentExePath)!;
        var currentPid = Environment.ProcessId;

        // If payload is a zip archive, extract it
        var extractFolder = tempDir;
        if (downloadFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            extractFolder = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractFolder);
            ZipFile.ExtractToDirectory(downloadFile, extractFolder, overwriteFiles: true);

            // Handle nested single root folder inside zip
            var subDirs = Directory.GetDirectories(extractFolder);
            if (subDirs.Length == 1 && Directory.GetFiles(extractFolder).Length == 0)
            {
                extractFolder = subDirs[0];
            }
        }

        // Generate PowerShell updater script that waits for current process to exit, swaps binaries, and relaunches
        var updateScriptPath = Path.Combine(tempDir, "apply_update.ps1");
        var scriptContent = $@"
# WinToolkit Pro Automatic Updater
$procId = {currentPid}
$targetDir = '{currentAppDir.Replace("'", "''")}'
$sourceDir = '{extractFolder.Replace("'", "''")}'
$exePath = '{currentExePath.Replace("'", "''")}'

# Wait for application process to exit
Write-Output 'Waiting for running WinToolkit Pro instance to terminate...'
$timeout = 30
while ((Get-Process -Id $procId -ErrorAction SilentlyContinue) -and ($timeout -gt 0)) {{
    Start-Sleep -Milliseconds 500
    $timeout--
}}
if (Get-Process -Id $procId -ErrorAction SilentlyContinue) {{
    Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
}}

# Copy new payload files over existing install
Write-Output 'Copying updated version files...'
Copy-Item -Path ""$sourceDir\*"" -Destination ""$targetDir"" -Recurse -Force -ErrorAction SilentlyContinue

# Unblock files to eliminate Windows SmartScreen Mark-of-the-Web
Get-ChildItem -Path ""$targetDir"" -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

# Launch new updated instance elevated
Write-Output 'Relaunching WinToolkit Pro...'
Start-Sleep -Milliseconds 500
Start-Process -FilePath $exePath -Verb RunAs

# Clean up updater temp folder
Start-Sleep -Seconds 3
Remove-Item -Path '{tempDir.Replace("'", "''")}' -Recurse -Force -ErrorAction SilentlyContinue
";

        await File.WriteAllTextAsync(updateScriptPath, scriptContent, Encoding.UTF8, ct);

        statusCallback?.Invoke("Applying update and restarting application...");

        // Launch update script detached as admin
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updateScriptPath}\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);

        // Terminate current process so the swap script can overwrite locked binaries
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            System.Windows.Application.Current.Shutdown();
        });

        Environment.Exit(0);
        return true;
    }

    private static bool IsVersionNewer(string current, string latest)
    {
        if (Version.TryParse(current, out var vCurrent) && Version.TryParse(latest, out var vLatest))
        {
            return vLatest > vCurrent;
        }
        return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
