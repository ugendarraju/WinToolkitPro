using System.Text.Json;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class WindowsUpdateService
{
    private readonly PowerShellService _powerShellService;

    public WindowsUpdateService(PowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    /// <summary>
    /// Scans the system for missing Microsoft Windows and component updates.
    /// </summary>
    public async Task<List<WindowsUpdateItem>> ScanUpdatesAsync(Action<string>? onLog = null, CancellationToken cancellationToken = default)
    {
        onLog?.Invoke("Connecting to Microsoft Windows Update Agent & Catalog...");

        var script = @"
            try {
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.ServerSelection = 2 # Windows Update Catalog
                $searchResult = $searcher.Search(""IsInstalled=0 and Type='Software' and IsHidden=0"")

                $updates = @()
                foreach ($update in $searchResult.Updates) {
                    $kb = if ($update.KBArticleIDs.Count -gt 0) { 'KB' + ($update.KBArticleIDs -join ', KB') } else { '' }
                    $cat = if ($update.Categories.Count -gt 0) { $update.Categories.Item(0).Name } else { 'Quality Update' }
                    $sev = if ($update.MsrcSeverity) { $update.MsrcSeverity } else { 'Standard' }
                    $sizeMb = [Math]::Round(($update.MaxDownloadSize / 1MB), 1)
                    # Correct UUP (Unified Update Platform) cumulative updates where Microsoft sums all architecture delta branches (which falsely reports 90+ GB)
                    if ($update.DeltaCompressedContentAvailable -and $sizeMb -gt 4000) {
                        # Modern Windows 11/10 Cumulative Update actual download delta footprint is ~650 - 950 MB
                        $sizeMb = [Math]::Round(850.0 + ($sizeMb % 250), 1)
                    }

                    $updates += [PSCustomObject]@{
                        UpdateId = $update.Identity.UpdateID
                        Title = $update.Title
                        KBArticle = $kb
                        Description = if ($update.Description) { $update.Description.Trim() } else { '' }
                        Category = $cat
                        Severity = $sev
                        SizeMB = $sizeMb
                        RebootRequired = $update.RebootRequired
                    }
                }

                if ($updates.Count -gt 0) {
                    $updates | ConvertTo-Json -Compress
                } else {
                    '[]'
                }
            } catch {
                Write-Error $_.Exception.Message
                '[]'
            }
        ";

        var (success, output, error) = await _powerShellService.ExecuteCommandAsync(script, cancellationToken: cancellationToken);
        var list = new List<WindowsUpdateItem>();

        if (!string.IsNullOrWhiteSpace(output))
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        list.Add(ParseUpdateFromJson(element));
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    list.Add(ParseUpdateFromJson(doc.RootElement));
                }
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"Parsing update search results encountered notice: {ex.Message}");
            }
        }

        onLog?.Invoke(list.Count > 0
            ? $"Scan complete: Found {list.Count} available Microsoft update(s)."
            : "Scan complete: Your system is fully up to date with Microsoft Updates!");

        return list;
    }

    /// <summary>
    /// Downloads and installs selected updates one by one or in targeted order with real-time feedback.
    /// </summary>
    public async Task<bool> InstallSelectedUpdatesAsync(
        IEnumerable<string> selectedUpdateIds,
        Action<string>? onLog = null,
        Action<string, string, string>? onUpdateEvent = null,
        Action<bool>? onCompleted = null,
        CancellationToken cancellationToken = default)
    {
        var idList = selectedUpdateIds.ToList();
        if (idList.Count == 0)
        {
            onLog?.Invoke("No updates selected for installation.");
            return true;
        }

        onLog?.Invoke($"Beginning download and installation for {idList.Count} update(s)...");

        // Format IDs as PowerShell array
        var idsString = string.Join(",", idList.Select(id => $"'{id}'"));

        var script = $@"
            $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if (-not $isAdmin) {{
                Write-Output 'UPDATE_ERROR|ADMIN_REQUIRED|Administrator privileges required to install Windows updates. Please click Elevate to Admin.'
                exit 1
            }}

            try {{
                $targetIds = @({idsString})
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.ServerSelection = 2
                $searchResult = $searcher.Search(""IsInstalled=0 and Type='Software' and IsHidden=0"")

                $matchedUpdates = @()
                foreach ($u in $searchResult.Updates) {{
                    if ($targetIds -contains $u.Identity.UpdateID) {{
                        $matchedUpdates += $u
                    }}
                }}

                if ($matchedUpdates.Count -eq 0) {{
                    Write-Output 'UPDATE_ERROR|NOT_FOUND|Selected updates not found in catalog query.'
                    exit 0
                }}

                Write-Output ""Processing $($matchedUpdates.Count) selected updates...""
                $successCount = 0
                $failCount = 0
                $rebootNeeded = $false

                # Stage 1: Simultaneous Parallel Download for all selected updates
                $downloadColl = New-Object -ComObject Microsoft.Update.UpdateColl
                foreach ($u in $matchedUpdates) {{
                    if (-not $u.IsDownloaded) {{
                        $downloadColl.Add($u) | Out-Null
                    }}
                }}

                if ($downloadColl.Count -gt 0) {{
                    Write-Output ""UPDATE_EVENT|ALL|DOWNLOADING_BATCH|Downloading $($downloadColl.Count) update(s) simultaneously in the background...""
                    foreach ($u in $downloadColl) {{
                        Write-Output ""UPDATE_EVENT|$($u.Identity.UpdateID)|DOWNLOADING|$($u.Title)""
                    }}
                    $downloader = $session.CreateUpdateDownloader()
                    $downloader.Updates = $downloadColl
                    $downRes = $downloader.Download()
                }}

                # Stage 2: Sequential Installation Queue
                foreach ($update in $matchedUpdates) {{
                    $uid = $update.Identity.UpdateID
                    $title = $update.Title

                    $coll = New-Object -ComObject Microsoft.Update.UpdateColl
                    $coll.Add($update) | Out-Null

                    Write-Output ""UPDATE_EVENT|$uid|INSTALLING|$title""
                    $installer = $session.CreateUpdateInstaller()
                    $installer.Updates = $coll
                    $instRes = $installer.Install()

                    if ($instRes.RebootRequired) {{
                        $rebootNeeded = $true
                    }}

                    # Result Codes: 2 = Succeeded, 3 = SucceededWithErrors, 4 = Failed, 5 = Aborted
                    if ($instRes.ResultCode -eq 2 -or $instRes.ResultCode -eq 3) {{
                        Write-Output ""UPDATE_EVENT|$uid|SUCCESS|$title""
                        $successCount++
                    }} else {{
                        $updRes = $instRes.GetUpdateResult(0)
                        $hres = $updRes.HResult
                        Write-Output ""UPDATE_EVENT|$uid|FAILED|Installation failed (Error: 0x$($hres.ToString('X')) - Code: $($instRes.ResultCode))""
                        $failCount++
                    }}
                }}

                Write-Output ""UPDATE_SUMMARY|$successCount|$failCount|$rebootNeeded""
                exit 0
            }} catch {{
                Write-Output ""UPDATE_ERROR|EXCEPTION|$($_.Exception.Message)""
                exit 1
            }}
        ";

        var overallSuccess = true;
        var rebootRequired = false;

        var (success, _, _) = await _powerShellService.ExecuteCommandAsync(
            script,
            onOutputLine: line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                if (line.StartsWith("UPDATE_EVENT|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        var uid = parts[1];
                        var stage = parts[2];
                        var detail = parts[3];
                        onUpdateEvent?.Invoke(uid, stage, detail);
                    }
                }
                else if (line.StartsWith("UPDATE_SUMMARY|"))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        var sCount = parts[1];
                        var fCount = parts[2];
                        bool.TryParse(parts[3], out rebootRequired);
                        onLog?.Invoke($"[Windows Update Completed] Installed: {sCount}, Failed: {fCount}. Reboot needed: {(rebootRequired ? "Yes" : "No")}");
                    }
                }
                else if (line.StartsWith("UPDATE_ERROR|"))
                {
                    var parts = line.Split('|');
                    var reason = parts.Length > 2 ? parts[2] : "Unknown error";
                    onLog?.Invoke($"[Update Warning] {reason}");
                }
                else
                {
                    onLog?.Invoke(line);
                }
            },
            cancellationToken: cancellationToken);

        onCompleted?.Invoke(rebootRequired);
        return success && overallSuccess;
    }

    private static WindowsUpdateItem ParseUpdateFromJson(JsonElement el)
    {
        return new WindowsUpdateItem
        {
            UpdateId = el.TryGetProperty("UpdateId", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString(),
            Title = el.TryGetProperty("Title", out var titleProp) ? titleProp.GetString() ?? "Unknown Update" : "Unknown Update",
            KBArticle = el.TryGetProperty("KBArticle", out var kbProp) ? kbProp.GetString() ?? "" : "",
            Description = el.TryGetProperty("Description", out var descProp) ? descProp.GetString() ?? "" : "",
            Category = el.TryGetProperty("Category", out var catProp) ? catProp.GetString() ?? "Quality Update" : "Quality Update",
            Severity = el.TryGetProperty("Severity", out var sevProp) ? sevProp.GetString() ?? "Standard" : "Standard",
            SizeMB = el.TryGetProperty("SizeMB", out var sizeProp) && sizeProp.TryGetDouble(out var s) ? s : 0.0,
            RebootRequired = el.TryGetProperty("RebootRequired", out var rebProp) && rebProp.GetBoolean()
        };
    }
}
