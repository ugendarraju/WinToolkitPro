using System.IO;
using System.Text.Json;
using WinToolkit.Models;

namespace WinToolkit.Services;

public class CatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async Task<List<AppItem>> LoadAppsAsync()
    {
        var filePath = FindDataFile("apps.json");
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var items = JsonSerializer.Deserialize<List<AppItem>>(json, JsonOptions);
                if (items != null) return items;
            }
            catch { }
        }

        return new List<AppItem>();
    }

    public async Task<List<TweakItem>> LoadTweaksAsync()
    {
        var filePath = FindDataFile("tweaks.json");
        if (File.Exists(filePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var items = JsonSerializer.Deserialize<List<TweakItem>>(json, JsonOptions);
                if (items != null) return items;
            }
            catch { }
        }

        return new List<TweakItem>();
    }

    private static string FindDataFile(string filename)
    {
        // Check current app base directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var directPath = Path.Combine(baseDir, "Data", filename);
        if (File.Exists(directPath)) return directPath;

        // Check relative folder if running in dev environment
        var parentPath = Path.Combine(baseDir, "..", "..", "..", "Data", filename);
        if (File.Exists(parentPath)) return Path.GetFullPath(parentPath);

        var workingPath = Path.Combine(Environment.CurrentDirectory, "Data", filename);
        if (File.Exists(workingPath)) return workingPath;

        return directPath;
    }
}
