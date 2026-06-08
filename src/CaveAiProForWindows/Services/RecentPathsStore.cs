using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Persists last opened CaveAI paths under LocalApplicationData.</summary>
public static class RecentPathsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    private static string StorePath =>
        Environment.GetEnvironmentVariable("CAVEAI_RECENT_PATH_OVERRIDE")
        ?? Path.Combine(StoreDirectory, "recent.json");

    public static IReadOnlyList<string> Load(int max = 12)
    {
        try
        {
            if (!File.Exists(StorePath)) return Array.Empty<string>();
            var json = File.ReadAllText(StorePath);
            var doc = JsonSerializer.Deserialize<RecentFileDto>(json, JsonOpts);
            if (doc?.Paths == null) return Array.Empty<string>();
            return doc.Paths.Where(RecentPathFileOps.Exists).Take(max).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public static void Push(string path, int max = 12)
    {
        try
        {
            var list = Load(max * 2).ToList();
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            SavePaths(list, max);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>Removes a path from the recent list only — does not delete the file on disk.</summary>
    public static void Remove(string path, int max = 12)
    {
        try
        {
            var list = ReadStoredPaths();
            if (list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) == 0)
                return;
            SavePaths(list, max);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>Updates a stored path after rename on disk.</summary>
    public static void ReplacePath(string oldPath, string newPath, int max = 12)
    {
        try
        {
            var list = ReadStoredPaths();
            var idx = list.FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = newPath;
            else
            {
                list.RemoveAll(p => string.Equals(p, newPath, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, newPath);
            }

            SavePaths(list, max);
        }
        catch
        {
            /* ignore */
        }
    }

    private static List<string> ReadStoredPaths()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new List<string>();
            var json = File.ReadAllText(StorePath);
            var doc = JsonSerializer.Deserialize<RecentFileDto>(json, JsonOpts);
            return doc?.Paths ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void SavePaths(List<string> list, int max)
    {
        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var dto = new RecentFileDto { Paths = list.Take(max).ToList() };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOpts));
    }

    private sealed class RecentFileDto
    {
        public List<string> Paths { get; set; } = new();
    }
}
