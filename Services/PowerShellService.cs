using System.Diagnostics;
using System.Text;

namespace WinToolkit.Services;

public class PowerShellService
{
    public async Task<(bool Success, string Output, string Error)> ExecuteCommandAsync(
        string command,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<(bool, string, string)>();
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // Encode script in base64 to avoid PowerShell quoting / escaping issues
        var bytes = Encoding.Unicode.GetBytes(command);
        var encodedCommand = Convert.ToBase64String(bytes);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
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
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch { }
            }))
            {
                await process.WaitForExitAsync(cancellationToken);
            }

            var success = process.ExitCode == 0;
            return (success, outputBuilder.ToString().Trim(), errorBuilder.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            return (false, "Execution cancelled by user.", "Cancelled");
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
        finally
        {
            process.Dispose();
        }
    }

    public async Task<bool> CreateRestorePointAsync(string description)
    {
        var script = $@"
            try {{
                Enable-ComputerRestore -Drive 'C:\' -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
                exit 0
            }} catch {{
                Write-Error $_.Exception.Message
                exit 1
            }}
        ";

        var (success, _, _) = await ExecuteCommandAsync(script);
        return success;
    }
}
