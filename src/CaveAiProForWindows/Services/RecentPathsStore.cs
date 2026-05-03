using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Persists last opened CaveAI paths under LocalApplicationData.</summary>
public static class RecentPathsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    private static string StorePath => Path.Combine(StoreDirectory, "recent.json");

    public static IReadOnlyList<string> Load(int max = 12)
    {
        try
        {
            if (!File.Exists(StorePath)) return Array.Empty<string>();
            var json = File.ReadAllText(StorePath);
            var doc = JsonSerializer.Deserialize<RecentFileDto>(json, JsonOpts);
            if (doc?.Paths == null) return Array.Empty<string>();
            return doc.Paths.Where(File.Exists).Take(max).ToList();
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
            Directory.CreateDirectory(StoreDirectory);
            var dto = new RecentFileDto { Paths = list.Take(max).ToList() };
            File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class RecentFileDto
    {
        public List<string> Paths { get; set; } = new();
    }
}
