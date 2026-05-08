using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Category of image asset we're trying to find inside an Android backup ZIP / sidecar JSON.
/// </summary>
public enum AndroidBackupImageCategory
{
    /// <summary>Satellite / aerial / x-ray basemap snapshot (single image overlaid on traverse).</summary>
    XRayBackdrop,

    /// <summary>Geology / Gemini photos (one per rock card, multiple per project).</summary>
    GeologyPhoto,
}

/// <summary>
/// One image candidate discovered from the backup payload. Either <see cref="RawValue"/> (path / URI to resolve
/// via <see cref="MapAssetOpener.TryEnsureLocalFilePath"/>) or <see cref="EmbeddedJson"/> (a JSON subtree that may
/// contain a <c>data:image</c> / base64 leaf to decode in-memory) is non-null.
/// </summary>
/// <param name="SourceLabel">Human-readable origin (e.g. <c>"ExtensionData[geminiSatelliteImageUri]"</c>) — useful for diagnostics.</param>
/// <param name="RawValue">Raw path/URI string from JSON or ZIP entry name; resolve via <see cref="MapAssetOpener"/>.</param>
/// <param name="EmbeddedJson">JSON subtree to scan for inline base64 images via <see cref="OfflineEmbeddedImageDecoder"/>.</param>
public sealed record AndroidBackupImageHint(string SourceLabel, string? RawValue, JsonElement? EmbeddedJson);

/// <summary>
/// Robust, **dynamic** discovery of X-Ray / Geology image assets inside an Android backup ZIP. Replaces the old
/// hardcoded <c>satellite_background.jpg</c> / <c>/satellite/</c> heuristics that missed Android-generated names
/// like <c>image_09019f.png</c> stored under <c>photos/</c> or <c>export_assets/maps/{cave}__{hash}/{NNN}_{slot}_{shortId}/{leaf}.ext</c>.
/// <para>
/// Discovery order (each hint is yielded once, callers stop at first successful resolve):
/// </para>
/// <list type="number">
///   <item><description>Explicit typed properties on <see cref="CaveProjectDocument"/> (e.g. <see cref="CaveProjectDocument.XrayBackdropImageUri"/>).</description></item>
///   <item><description><c>data.json</c> deep harvest under any property whose name matches the category keyword set.</description></item>
///   <item><description><c>map_inventory.json</c> rows (matched by project name) whose <c>slot</c> matches the category.</description></item>
///   <item><description><see cref="CaveProjectDocument.Rocks"/> / <c>fieldCatalogEntries</c> arrays for geology photos.</description></item>
///   <item><description>ZIP entry path scan (broad keyword match, <c>photos/</c> for geology, image extension required).</description></item>
/// </list>
/// </summary>
public static class AndroidBackupImageDiscovery
{
    private static readonly string[] ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".geotiff",
    ];

    private static readonly string[] XRayKeywords =
    [
        "xray", "x-ray", "x_ray", "satellite", "aerial", "basemap", "base_map",
        "ortho", "orthophoto", "snapshot", "mapsnapshot", "map_snapshot",
        "visionsatellite", "geminisatellite", "satelliteoverlay", "skyview",
    ];

    private static readonly string[] GeologyKeywords =
    [
        "geolog", "gemini", "rock", "mineral", "petrolog", "biolog", "lithology",
        "texture", "speleothem", "outcrop",
    ];

    private static readonly string[] GeologyAnalysisKeywords =
    [
        "geolog", "gemini", "rock", "mineral", "petrolog", "biolog", "lithology",
        "analysis", "summary", "report", "vision", "aianalysis", "cloud",
    ];

    /// <summary>
    /// Yields candidate image hints for the given category, in roughly decreasing relevance order. Caller is
    /// expected to attempt resolution one by one and stop at the first that loads a usable bitmap. Hints are
    /// de-duplicated by their resolution-key (raw value or embedded-JSON identity).
    /// </summary>
    public static IEnumerable<AndroidBackupImageHint> Enumerate(
        AndroidBackupImageCategory category,
        CaveProjectDocument project,
        string? zipPath,
        IEnumerable? mapInventory)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AndroidBackupImageHint? Make(string label, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var trimmed = raw.Trim();
            if (!seen.Add("R|" + trimmed))
                return null;
            return new AndroidBackupImageHint(label, trimmed, null);
        }

        AndroidBackupImageHint? MakeEmbedded(string label, JsonElement el)
        {
            var key = "E|" + label + "|" + el.GetRawText().GetHashCode().ToString("X");
            if (!seen.Add(key))
                return null;
            return new AndroidBackupImageHint(label, null, el);
        }

        if (category == AndroidBackupImageCategory.XRayBackdrop)
        {
            foreach (var (label, value) in EnumerateExplicitXRayProperties(project))
            {
                var h = Make(label, value);
                if (h != null) yield return h;
            }
        }

        if (project.ExtensionData != null)
        {
            foreach (var kv in project.ExtensionData)
            {
                if (!KeyMatchesCategory(kv.Key, category))
                    continue;

                if (LooksLikeEmbeddedImageContainer(kv.Value))
                {
                    var emb = MakeEmbedded($"ExtensionData[{kv.Key}] (embedded)", kv.Value);
                    if (emb != null) yield return emb;
                }

                foreach (var path in HarvestStringPaths(kv.Value))
                {
                    if (!LooksLikeImageReference(path))
                        continue;
                    var h = Make($"ExtensionData[{kv.Key}]", path);
                    if (h != null) yield return h;
                }
            }
        }

        if (category == AndroidBackupImageCategory.GeologyPhoto)
        {
            foreach (var label in EnumerateExplicitGeologyPhotoProperties(project))
            {
                var h = Make(label.Label, label.Value);
                if (h != null) yield return h;
            }

            foreach (var (label, path) in EnumerateRockAndCatalogPhotos(project))
            {
                var h = Make(label, path);
                if (h != null) yield return h;
            }
        }

        if (mapInventory != null)
        {
            foreach (var (slotLabel, path) in EnumerateInventoryHints(mapInventory, project, category))
            {
                var h = Make(slotLabel, path);
                if (h != null) yield return h;
            }
        }

        if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
        {
            foreach (var entryName in EnumerateZipImageEntries(zipPath, category))
            {
                var h = Make($"ZipEntry[{entryName}]", entryName);
                if (h != null) yield return h;
            }
        }
    }

    /// <summary>
    /// Resolves the (potentially dynamic) path or URI a hint points at to an absolute local file path on disk.
    /// Wraps <see cref="MapAssetOpener.TryEnsureLocalFilePath"/> + sidecar directory inference so callers don't
    /// have to repeat that boilerplate. Returns <c>null</c> for embedded-JSON hints (caller should handle those
    /// via <see cref="OfflineEmbeddedImageDecoder"/>).
    /// </summary>
    public static string? TryResolveLocalFile(
        AndroidBackupImageHint hint,
        CaveProjectDocument project,
        string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(hint.RawValue))
            return null;
        var jsonDir = TryResolveJsonSidecarDirectory(project);
        if (!MapAssetOpener.TryEnsureLocalFilePath(hint.RawValue, zipPath, out var localPath, out _, jsonDir))
            return null;
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            return null;
        return localPath;
    }

    /// <summary>
    /// Best-effort retrieval of the Gemini / on-device geology analysis text. Tries (1) the explicit
    /// <see cref="CaveProjectDocument.GeminiGeologyAnalysisText"/> family, then (2) deep harvesting from
    /// <see cref="CaveProjectDocument.ExtensionData"/> under broader keyword keys, then (3) a sidecar
    /// <c>geology*</c> / <c>gemini*</c> text/json/md entry inside the open ZIP.
    /// </summary>
    public static string? FindGeologyAnalysisText(CaveProjectDocument project, string? zipPath)
    {
        foreach (var explicitText in new[]
                 {
                     project.GeminiGeologyAnalysisText,
                     project.GeminiGeologyAnalysis,
                     project.GeminiAnalysisText,
                     project.AiGeologyAnalysisText,
                     project.CloudGeologyAnalysisText,
                 })
        {
            if (!string.IsNullOrWhiteSpace(explicitText) && explicitText.Trim().Length >= 8)
                return explicitText.Trim();
        }

        if (project.GeminiGeologyAnalysisJson.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            string? best = null;
            HarvestPossibleText(project.GeminiGeologyAnalysisJson, ref best, "geminiGeologyAnalysisJson");
            if (!string.IsNullOrWhiteSpace(best))
                return best;
        }

        if (project.ExtensionData != null && project.ExtensionData.Count > 0)
        {
            string? best = null;
            foreach (var kv in project.ExtensionData)
            {
                if (!IsAnalysisKey(kv.Key))
                    continue;
                HarvestPossibleText(kv.Value, ref best, kv.Key);
            }

            if (!string.IsNullOrWhiteSpace(best))
                return best;
        }

        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return null;

        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/').ToLowerInvariant();
                if (name.EndsWith('/'))
                    continue;
                if (!IsAnalysisKey(name))
                    continue;
                if (!name.EndsWith(".txt", StringComparison.Ordinal) &&
                    !name.EndsWith(".json", StringComparison.Ordinal) &&
                    !name.EndsWith(".md", StringComparison.Ordinal))
                    continue;
                using var sr = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
                var text = sr.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(text) && text.Trim().Length >= 8)
                    return text.Trim();
            }
        }
        catch
        {
            // Keep the UI alive on partial / malformed backups.
        }

        return null;
    }

    /// <summary>
    /// Counts raster image entries inside a ZIP — used by views to render diagnostic placeholders that hint
    /// "the ZIP does contain images, but none matched this category". Returns 0 on any IO error.
    /// </summary>
    public static int CountRasterImageEntries(string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return 0;
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            return zip.Entries.Count(e =>
                !string.IsNullOrEmpty(e.Name) &&
                IsImageExtension(Path.GetExtension(e.FullName)));
        }
        catch
        {
            return 0;
        }
    }

    private static IEnumerable<(string Label, string? Value)> EnumerateExplicitXRayProperties(CaveProjectDocument p)
    {
        yield return ("xrayBackdropImageUri", p.XrayBackdropImageUri);
        yield return ("geminiSatelliteImageUri", p.GeminiSatelliteImageUri);
        yield return ("satelliteSnapshotImageUri", p.SatelliteSnapshotImageUri);
        yield return ("cloudSatelliteSnapshotUri", p.CloudSatelliteSnapshotUri);
    }

    private static IEnumerable<(string Label, string Value)> EnumerateExplicitGeologyPhotoProperties(CaveProjectDocument p)
    {
        if (p.GeminiGeologyPhotoUris is { Count: > 0 } a)
        {
            for (var i = 0; i < a.Count; i++)
            {
                var v = a[i];
                if (!string.IsNullOrWhiteSpace(v))
                    yield return ($"geminiGeologyPhotoUris[{i}]", v);
            }
        }

        if (p.AiGeologyPhotoUris is { Count: > 0 } b)
        {
            for (var i = 0; i < b.Count; i++)
            {
                var v = b[i];
                if (!string.IsNullOrWhiteSpace(v))
                    yield return ($"aiGeologyPhotoUris[{i}]", v);
            }
        }
    }

    private static IEnumerable<(string Label, string Path)> EnumerateRockAndCatalogPhotos(CaveProjectDocument p)
    {
        if (p.Rocks is { ValueKind: JsonValueKind.Array } rocks)
        {
            var i = 0;
            foreach (var rock in rocks.EnumerateArray())
            {
                if (rock.ValueKind == JsonValueKind.Object)
                {
                    foreach (var key in new[] { "imageUri", "photoUri", "uri", "path", "imagePath", "photoPath", "thumbnailUri" })
                    {
                        if (rock.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                        {
                            var v = val.GetString();
                            if (!string.IsNullOrWhiteSpace(v) && LooksLikeImageReference(v))
                                yield return ($"rocks[{i}].{key}", v.Trim());
                        }
                    }
                }

                i++;
            }
        }

        if (p.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } cat)
        {
            var i = 0;
            foreach (var entry in cat.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("photoReference", out var pr) &&
                    pr.ValueKind == JsonValueKind.String)
                {
                    var v = pr.GetString();
                    if (!string.IsNullOrWhiteSpace(v) && LooksLikeImageReference(v))
                        yield return ($"fieldCatalogEntries[{i}].photoReference", v.Trim());
                }

                i++;
            }
        }
    }

    private static IEnumerable<(string Label, string Path)> EnumerateInventoryHints(
        IEnumerable mapInventory,
        CaveProjectDocument project,
        AndroidBackupImageCategory category)
    {
        var caveName = (project.Name ?? "").Trim();
        foreach (var row in mapInventory)
        {
            if (row is not MapInventoryRow inv)
                continue;
            if (!string.Equals(inv.ProjectName.Trim(), caveName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(inv.PathInBackupOrUrl))
                continue;
            if (!SlotMatchesCategory(inv.Slot, category))
                continue;
            yield return ($"map_inventory[{inv.Slot}]", inv.PathInBackupOrUrl);
        }
    }

    private static IEnumerable<string> EnumerateZipImageEntries(string zipPath, AndroidBackupImageCategory category)
    {
        var hits = new List<string>();
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var e in zip.Entries)
            {
                if (string.IsNullOrEmpty(e.Name))
                    continue;
                var full = e.FullName.Replace('\\', '/');
                if (!IsImageExtension(Path.GetExtension(full)))
                    continue;
                if (!ZipPathMatchesCategory(full, category))
                    continue;
                hits.Add(full);
            }
        }
        catch
        {
            // Keep UI alive on a malformed ZIP.
        }

        return hits;
    }

    private static bool KeyMatchesCategory(string key, AndroidBackupImageCategory category)
    {
        var k = key.ToLowerInvariant();
        var keywords = category == AndroidBackupImageCategory.XRayBackdrop ? XRayKeywords : GeologyKeywords;
        foreach (var kw in keywords)
        {
            if (k.Contains(kw, StringComparison.Ordinal))
                return true;
        }

        if (category == AndroidBackupImageCategory.XRayBackdrop)
        {
            // Generic map+image / map+background fallback (preserves old heuristic).
            if (k.Contains("map", StringComparison.Ordinal) &&
                (k.Contains("image", StringComparison.Ordinal) ||
                 k.Contains("background", StringComparison.Ordinal) ||
                 k.Contains("backdrop", StringComparison.Ordinal)))
                return true;
        }

        return false;
    }

    private static bool SlotMatchesCategory(string? slot, AndroidBackupImageCategory category)
    {
        if (string.IsNullOrWhiteSpace(slot))
            return false;
        var s = slot.ToLowerInvariant();
        var keywords = category == AndroidBackupImageCategory.XRayBackdrop ? XRayKeywords : GeologyKeywords;
        foreach (var kw in keywords)
        {
            if (s.Contains(kw, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool ZipPathMatchesCategory(string fullPath, AndroidBackupImageCategory category)
    {
        var p = fullPath.ToLowerInvariant();
        var keywords = category == AndroidBackupImageCategory.XRayBackdrop ? XRayKeywords : GeologyKeywords;
        foreach (var kw in keywords)
        {
            if (p.Contains(kw, StringComparison.Ordinal))
                return true;
        }

        // For geology, also accept anything under photos/ (Android's typical photo bucket).
        if (category == AndroidBackupImageCategory.GeologyPhoto && p.Contains("photos/", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool LooksLikeImageReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var t = raw.Trim();
        if (t.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return IsImageExtension(Path.GetExtension(t.TrimEnd('/')));
        return IsImageExtension(Path.GetExtension(t));
    }

    private static bool IsImageExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return false;
        var e = ext.ToLowerInvariant();
        foreach (var img in ImageExtensions)
        {
            if (string.Equals(e, img, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsAnalysisKey(string key)
    {
        var k = key.ToLowerInvariant();
        foreach (var kw in GeologyAnalysisKeywords)
        {
            if (k.Contains(kw, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>True if the JSON subtree contains at least one obvious base64 / data-URI image leaf.</summary>
    private static bool LooksLikeEmbeddedImageContainer(JsonElement el)
    {
        return WalkContainsDataUri(el, depth: 0);

        static bool WalkContainsDataUri(JsonElement e, int depth)
        {
            if (depth > 6)
                return false;
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                {
                    var s = e.GetString();
                    return !string.IsNullOrEmpty(s) && s!.StartsWith("data:image", StringComparison.OrdinalIgnoreCase);
                }
                case JsonValueKind.Array:
                    foreach (var c in e.EnumerateArray())
                        if (WalkContainsDataUri(c, depth + 1))
                            return true;
                    return false;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                        if (WalkContainsDataUri(p.Value, depth + 1))
                            return true;
                    return false;
                default:
                    return false;
            }
        }
    }

    private static IEnumerable<string> HarvestStringPaths(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    yield return s;
                break;
            }
            case JsonValueKind.Array:
                foreach (var c in el.EnumerateArray())
                foreach (var s in HarvestStringPaths(c))
                    yield return s;
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                foreach (var s in HarvestStringPaths(p.Value))
                    yield return s;
                break;
        }
    }

    private static void HarvestPossibleText(JsonElement el, ref string? best, string? keyHint)
    {
        var minLen = !string.IsNullOrEmpty(keyHint) && IsAnalysisKey(keyHint!) ? 8 : 24;
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return;
                var t = s.Trim();
                if (t.Length < minLen)
                    return;
                if (best == null || t.Length > best.Length)
                    best = t;
                return;
            }
            case JsonValueKind.Array:
                foreach (var c in el.EnumerateArray())
                    HarvestPossibleText(c, ref best, keyHint);
                return;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                    HarvestPossibleText(p.Value, ref best, p.Name);
                return;
        }
    }

    private static string? TryResolveJsonSidecarDirectory(CaveProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(project.LoadedFromFile))
            return null;
        if (!project.LoadedFromFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var full = Path.GetFullPath(project.LoadedFromFile);
            return File.Exists(full) ? Path.GetDirectoryName(full) : null;
        }
        catch
        {
            return null;
        }
    }
}
