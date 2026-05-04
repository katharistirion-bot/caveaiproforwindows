using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Loads the same JSON the Android app writes: <c>caveai_database_v1.json</c> / <c>data.json</c> as a <b>JSON array</b> of projects,
/// optional wrappers like <c>{"projects":[...]}</c>, a <b>single project object</b> with a <c>shots</c> array, or <c>data.json</c> inside a CaveAI ZIP.
/// </summary>
public static class ExplorationDataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Preferred ZIP entry names for the Gson project list (first match wins).</summary>
    private static readonly string[] DataJsonEntryNames =
    [
        "data.json",
    ];

    /// <summary>If Android wraps the array in an object, these property names are tried (case-insensitive).</summary>
    private static readonly string[] WrappedProjectArrayPropertyNames =
    [
        "projects",
        "caves",
        "data",
        "surveyProjects",
        "caveProjects",
        "surveyData",
        "projectList",
    ];

    public static IReadOnlyList<CaveProjectDocument> LoadFromJsonFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var normalized = NormalizeToProjectArrayJson(text, Path.GetFileName(path));
        return DeserializeProjectList(normalized);
    }

    public static IReadOnlyList<CaveProjectDocument> LoadFromCaveAiBackupZip(string path)
    {
        var (projects, _) = LoadFromCaveAiBackupZipWithRaw(path);
        return projects;
    }

    /// <summary>Reads <c>data.json</c> from a CaveAI backup ZIP and returns both deserialized projects and the raw UTF-8 text (for analytics).</summary>
    public static (List<CaveProjectDocument> Projects, string RawJson) LoadFromCaveAiBackupZipWithRaw(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var entry = FindDataJsonZipEntry(zip)
            ?? throw new InvalidDataException(
                "ZIP has no data.json entry (any casing) — pick a CaveAI Pro backup export (Downloads/CaveAI/*.zip).");
        using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
        var text = sr.ReadToEnd();
        var normalized = NormalizeToProjectArrayJson(text, "data.json");
        return (DeserializeProjectList(normalized), normalized);
    }

    /// <summary>Deserializes the same JSON array as <see cref="LoadFromJsonFile"/> without reading from disk.</summary>
    public static List<CaveProjectDocument> DeserializeProjectsFromText(string json)
    {
        var normalized = NormalizeToProjectArrayJson(json, "(inline)");
        return DeserializeProjectList(normalized);
    }

    public static IReadOnlyList<CaveProjectDocument> LoadAuto(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".zip" => LoadFromCaveAiBackupZip(path),
            ".json" => LoadFromJsonFile(path),
            _ => throw new NotSupportedException("Open a .json database file or a CaveAI .zip backup."),
        };
    }

    /// <summary>
    /// Finds <c>data.json</c> the way common Android ZIP writers emit it: exact match first, then any entry
    /// whose path equals <c>data.json</c> ignoring case (handles <c>Data.JSON</c>, <c>./data.json</c>).
    /// </summary>
    public static ZipArchiveEntry? FindDataJsonZipEntry(ZipArchive zip)
    {
        ZipArchiveEntry? fallback = null;
        foreach (var name in DataJsonEntryNames)
        {
            var exact = zip.GetEntry(name);
            if (exact != null)
                return exact;
        }

        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name))
                continue;
            var full = NormalizeZipEntryPath(e.FullName);
            if (string.IsNullOrEmpty(full))
                continue;
            if (!string.Equals(Path.GetFileName(full), "data.json", StringComparison.OrdinalIgnoreCase))
                continue;
            // Prefer root-level data.json over nested paths
            if (string.Equals(full, "data.json", StringComparison.OrdinalIgnoreCase))
                return e;
            fallback ??= e;
        }

        return fallback;
    }

    public static string NormalizeZipEntryPath(string fullName)
    {
        var s = fullName.Replace('\\', '/').Trim();
        while (s.StartsWith("./", StringComparison.Ordinal))
            s = s[2..];
        return s.TrimStart('/');
    }

    /// <summary>
    /// Android Gson export is a top-level JSON <b>array</b> of projects. Some pipelines wrap it as
    /// <c>{"projects":[...]}</c>; this returns the array fragment as text for <see cref="DeserializeProjectList"/>.
    /// </summary>
    public static string NormalizeToProjectArrayJson(string utf8Text, string? sourceLabelForErrors)
    {
        var trimmed = utf8Text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.Length == 0)
            throw new InvalidDataException(FormatJsonError(sourceLabelForErrors, "empty file"));

        if (trimmed[0] == '[')
        {
            using var probe = JsonDocument.Parse(trimmed);
            if (probe.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(FormatJsonError(sourceLabelForErrors, "root '[' is not a JSON array"));
            return trimmed;
        }

        using var doc = JsonDocument.Parse(trimmed);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return trimmed;

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrap in WrappedProjectArrayPropertyNames)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (!prop.Name.Equals(wrap, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (prop.Value.ValueKind != JsonValueKind.Array)
                        continue;
                    return prop.Value.GetRawText();
                }
            }

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array)
                    continue;
                if (prop.Value.GetArrayLength() == 0)
                    continue;
                // Single obvious wrapper: { "anything": [ { ... }, ... ] }
                var objCount = 0;
                foreach (var p in root.EnumerateObject())
                    objCount++;
                if (objCount == 1)
                    return prop.Value.GetRawText();
            }

            // One Gson CaveProject as a single object (some exports / tools emit `{ "name":..., "shots":[...] }` without an array wrapper).
            if (root.TryGetProperty("shots", out var shotsEl) && shotsEl.ValueKind == JsonValueKind.Array)
                return "[" + root.GetRawText() + "]";
        }

        throw new InvalidDataException(
            FormatJsonError(
                sourceLabelForErrors,
                "expected a JSON array of caves [ {...}, ... ] or an object with a \"projects\" (or similar) array — same as CaveAI Pro Gson export"));
    }

    private static string FormatJsonError(string? sourceLabelForErrors, string message)
    {
        var src = string.IsNullOrEmpty(sourceLabelForErrors) ? "JSON" : sourceLabelForErrors;
        return $"{src}: {message}";
    }

    private static List<CaveProjectDocument> DeserializeProjectList(string json)
    {
        var list = JsonSerializer.Deserialize<List<CaveProjectDocument>>(json, JsonOptions);
        if (list == null)
            return new List<CaveProjectDocument>();
        foreach (var p in list)
            ShotImportNormalizer.NormalizeProjectShots(p);
        return list;
    }
}
