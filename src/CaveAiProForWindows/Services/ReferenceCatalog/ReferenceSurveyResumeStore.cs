using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Maps reference catalog pin id → last Android backup ZIP path for resume-after-restart.</summary>
public static class ReferenceSurveyResumeStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StoreDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    private static string StorePath =>
        Environment.GetEnvironmentVariable("CAVEAI_REFERENCE_RESUME_STORE")
        ?? Path.Combine(StoreDirectory, "reference-survey-resume.json");

    public static void Remember(string referenceId, string zipPath)
    {
        if (string.IsNullOrWhiteSpace(referenceId) || string.IsNullOrWhiteSpace(zipPath))
            return;
        try
        {
            Directory.CreateDirectory(StoreDirectory);
            var map = LoadMap();
            map[referenceId.Trim()] = Path.GetFullPath(zipPath);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(map, JsonOpts));
        }
        catch
        {
            /* best-effort */
        }
    }

    public static string? TryGetPath(string referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
            return null;
        try
        {
            var map = LoadMap();
            if (!map.TryGetValue(referenceId.Trim(), out var path))
                return null;
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> LoadMap()
    {
        if (!File.Exists(StorePath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var json = File.ReadAllText(StorePath);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
        return map != null
            ? new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}