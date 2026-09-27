using WinToolkit.Models;

namespace WinToolkit.Services;

public class TweakService
{
    private readonly PowerShellService _powerShellService;

    public TweakService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    public async Task<bool> ApplyTweakAsync(
        TweakItem tweak,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tweak.ApplyScript))
        {
            tweak.Status = "No script defined";
            return false;
        }

        tweak.IsBusy = true;
        tweak.Status = "Applying...";
        onLog?.Invoke($"Applying tweak: {tweak.Title}...");

        var (success, output, error) = await _powerShellService.ExecuteCommandAsync(
            tweak.ApplyScript,
            onOutputLine: line => onLog?.Invoke($"[{tweak.Title}] {line}"),
            cancellationToken: cancellationToken);

        tweak.IsBusy = false;
        if (success)
        {
            tweak.IsApplied = true;
            tweak.Status = "Applied";
            onLog?.Invoke($"Successfully applied: {tweak.Title}");
            return true;
        }
        else
        {
            tweak.Status = "Failed";
            onLog?.Invoke($"Failed to apply {tweak.Title}: {error}");
            return false;
        }
    }

    public async Task<bool> RevertTweakAsync(
        TweakItem tweak,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tweak.RevertScript))
        {
            tweak.Status = "No revert script defined";
            return false;
        }

        tweak.IsBusy = true;
        tweak.Status = "Reverting...";
        onLog?.Invoke($"Reverting tweak: {tweak.Title}...");

        var (success, output, error) = await _powerShellService.ExecuteCommandAsync(
            tweak.RevertScript,
            onOutputLine: line => onLog?.Invoke($"[{tweak.Title}] {line}"),
            cancellationToken: cancellationToken);

        tweak.IsBusy = false;
        if (success)
        {
            tweak.IsApplied = false;
            tweak.Status = "Reverted (Default)";
            onLog?.Invoke($"Successfully reverted: {tweak.Title}");
            return true;
        }
        else
        {
            tweak.Status = "Failed to revert";
            onLog?.Invoke($"Failed to revert {tweak.Title}: {error}");
            return false;
        }
    }

    public async Task<bool> CreateSystemRestorePointAsync(string description)
    {
        return await _powerShellService.CreateRestorePointAsync(description);
    }
}
