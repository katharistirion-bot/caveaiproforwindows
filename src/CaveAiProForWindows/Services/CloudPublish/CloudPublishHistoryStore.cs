using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

public static class CloudPublishHistoryStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows", "publish-history.json");

    public static IReadOnlyList<CloudPublishHistoryEntry> LoadAll()
    {
        try
        {
            if (!File.Exists(StorePath)) return [];
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<CloudPublishHistoryEntry>>(json, JsonOpts) ?? [];
        }
        catch { return []; }
    }

    public static IReadOnlyList<CloudPublishHistoryEntry> ForProject(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return [];

        return LoadAll()
            .Where(e => string.Equals(e.ProjectName, projectName.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.PublishedAtUtc)
            .ToList();
    }

    public static void Record(string projectName, string publishedDocId, string? caveName = null)
    {
        if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(publishedDocId)) return;
        var entries = LoadAll().ToList();
        entries.RemoveAll(e => string.Equals(e.ProjectName, projectName.Trim(), StringComparison.OrdinalIgnoreCase));
        entries.Insert(0, new CloudPublishHistoryEntry
        {
            ProjectName = projectName.Trim(),
            PublishedDocId = publishedDocId.Trim(),
            CaveName = caveName?.Trim(),
            PublishedAtUtc = DateTimeOffset.UtcNow,
        });
        while (entries.Count > 80) entries.RemoveAt(entries.Count - 1);
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(entries, JsonOpts));
        }
        catch { }
    }
}

public sealed class CloudPublishHistoryEntry
{
    public string ProjectName { get; set; } = "";
    public string PublishedDocId { get; set; } = "";
    public string? CaveName { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}