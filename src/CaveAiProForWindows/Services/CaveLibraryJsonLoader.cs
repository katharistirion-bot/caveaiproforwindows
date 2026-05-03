using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Loads Android <c>cave_library.json</c> (array of <see cref="KnownCaveRecord"/>) from disk or inside a CaveAI backup ZIP.</summary>
public static class CaveLibraryJsonLoader
{
    public const string CaveLibraryEntryName = "cave_library.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<KnownCaveRecord> TryLoadFromZip(string zipPath)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = FindCaveLibraryZipEntry(zip);
            if (entry == null)
                return Array.Empty<KnownCaveRecord>();
            using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
            return TryDeserializeKnownCaves(sr.ReadToEnd(), zipPath);
        }
        catch
        {
            return Array.Empty<KnownCaveRecord>();
        }
    }

    public static ZipArchiveEntry? FindCaveLibraryZipEntry(ZipArchive zip)
    {
        var exact = zip.GetEntry(CaveLibraryEntryName);
        if (exact != null)
            return exact;
        ZipArchiveEntry? fallback = null;
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name))
                continue;
            var full = ExplorationDataLoader.NormalizeZipEntryPath(e.FullName);
            if (!string.Equals(Path.GetFileName(full), CaveLibraryEntryName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(full, CaveLibraryEntryName, StringComparison.OrdinalIgnoreCase))
                return e;
            fallback ??= e;
        }

        return fallback;
    }

    public static IReadOnlyList<KnownCaveRecord> TryDeserializeKnownCaves(string utf8Text, string? loadedFromFile = null)
    {
        try
        {
            var trimmed = utf8Text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            if (trimmed.Length == 0)
                return Array.Empty<KnownCaveRecord>();
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<KnownCaveRecord>();
            var list = new List<KnownCaveRecord>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                var r = el.Deserialize<KnownCaveRecord>(JsonOptions);
                if (r == null || string.IsNullOrWhiteSpace(r.Name))
                    continue;
                if (el.TryGetProperty("magneticHints", out var mh) && mh.ValueKind == JsonValueKind.Array)
                    r.MagneticHintsCount = mh.GetArrayLength();
                if (!string.IsNullOrEmpty(loadedFromFile))
                    r.LoadedFromFile = loadedFromFile;
                list.Add(r);
            }

            return list;
        }
        catch
        {
            return Array.Empty<KnownCaveRecord>();
        }
    }
}
