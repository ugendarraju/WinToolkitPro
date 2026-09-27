using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinToolkit.Models;

namespace WinToolkit.Services;

public partial class WingetService
{
    private static readonly Regex ProgressRegex = new(@"(\d{1,3})%", RegexOptions.Compiled);

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

            // Parse progress percentage
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

            // Winget exit codes:
            // 0 = Success
            // -1978335189 (0x8A15002B) = Already installed
            // -1978335175 (0x8A150039) = No applicable update found
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
}
