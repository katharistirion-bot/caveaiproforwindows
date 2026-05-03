using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Collects map / overlay / raster URIs and file paths from Gson <see cref="CaveProjectDocument.ExtensionData"/>
/// and <see cref="CaveProjectDocument.VectorLines"/> (library cartography, TLS mesh, LIDAR, symbols, sketches, tracks, etc.).
/// </summary>
public static class MapAssetsCollector
{
    private static readonly HashSet<string> DeepHarvestRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "mapSymbols",
        "sketches",
        "sectionSketches",
        "trackPoints",
    };

    /// <summary>Keys handled in the first explicit pass (avoid duplicate deep scan).</summary>
    private static readonly HashSet<string> SkipLooseNestedPass = new(StringComparer.OrdinalIgnoreCase)
    {
        "publicLibraryCartographyUris",
        "cartographyTlsMeshObjUri",
        "surfaceLidarRaster",
        "mapSymbols",
        "sketches",
        "sectionSketches",
        "trackPoints",
    };

    /// <param name="includeTraverseShotMedia">
    /// When true, includes <c>shots[i].photos[j]</c> and <c>audioMemoUri</c> (for full backup CSV audits).
    /// When false (default), the Maps tab stays focused on cartography / rasters / mesh / library assets.
    /// </param>
    public static IReadOnlyList<MapAssetRow> Collect(
        IEnumerable<CaveProjectDocument> projects,
        bool includeTraverseShotMedia = false)
    {
        var rows = new List<MapAssetRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var cave = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name;
            var src = p.LoadedFromFile ?? "";

            void Add(string category, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return;
                var v = value.Trim();
                if (!seen.Add($"{category}|{v}"))
                    return;
                rows.Add(new MapAssetRow(cave, category, v, src));
            }

            if (p.ExtensionData != null)
            {
                if (p.ExtensionData.TryGetValue("publicLibraryCartographyUris", out var libCart) &&
                    libCart.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var el in libCart.EnumerateArray())
                    {
                        var s = JsonString(el);
                        if (!string.IsNullOrEmpty(s))
                            Add($"publicLibraryCartographyUris[{i}]", s);
                        i++;
                    }
                }

                if (p.ExtensionData.TryGetValue("cartographyTlsMeshObjUri", out var mesh) && mesh.ValueKind == JsonValueKind.String)
                    Add("cartographyTlsMeshObjUri", mesh.GetString());

                if (p.ExtensionData.TryGetValue("surfaceLidarRaster", out var slr))
                    CollectFromJsonValue(rows, seen, cave, src, "surfaceLidarRaster", slr, 0);

                foreach (var kv in p.ExtensionData)
                {
                    var key = kv.Key;
                    if (SkipLooseNestedPass.Contains(key))
                        continue;
                    if (!LooksLikeMapRelatedKey(key))
                        continue;
                    CollectFromJsonValue(rows, seen, cave, src, key, kv.Value, 0);
                }

                foreach (var key in DeepHarvestRootKeys)
                {
                    if (!p.ExtensionData.TryGetValue(key, out var root))
                        continue;
                    HarvestAssetStrings(rows, seen, cave, src, key, root, "", 0, 12);
                }

                foreach (var kv in p.ExtensionData)
                {
                    if (SkipLooseNestedPass.Contains(kv.Key))
                        continue;
                    if (!ExtensionKeySuggestsNestedMapAssets(kv.Key))
                        continue;
                    HarvestAssetStrings(rows, seen, cave, src, kv.Key, kv.Value, "", 0, 6);
                }

                foreach (var kv in p.ExtensionData)
                {
                    if (kv.Value.ValueKind != JsonValueKind.String)
                        continue;
                    var key = kv.Key;
                    if (key.Equals("linkedLibraryCaveId", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var s = kv.Value.GetString();
                    if (string.IsNullOrWhiteSpace(s))
                        continue;
                    var uriLikeKey = key.EndsWith("Uri", StringComparison.OrdinalIgnoreCase) ||
                                     key.EndsWith("Url", StringComparison.OrdinalIgnoreCase);
                    if (IsProbableAssetPathOrUri(s))
                    {
                        Add(key, s);
                        continue;
                    }

                    if (!uriLikeKey)
                        continue;
                    var t = s.Trim();
                    if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                        t.IndexOfAny(['/', '\\']) >= 0 ||
                        t.Contains(':', StringComparison.Ordinal))
                        Add(key, s);
                }
            }

            if (includeTraverseShotMedia)
            {
                for (var si = 0; si < p.Shots.Count; si++)
                {
                    var shot = p.Shots[si];
                    for (var pi = 0; pi < shot.Photos.Count; pi++)
                    {
                        var ph = shot.Photos[pi];
                        if (!string.IsNullOrWhiteSpace(ph))
                            Add($"shots[{si}].photos[{pi}]", ph);
                    }

                    if (!string.IsNullOrWhiteSpace(shot.AudioMemoUri))
                        Add($"shots[{si}].audioMemoUri", shot.AudioMemoUri);
                }
            }

            if (p.Rocks is { ValueKind: JsonValueKind.Array } rockArr)
            {
                var ri = 0;
                foreach (var rel in rockArr.EnumerateArray())
                {
                    if (rel.ValueKind == JsonValueKind.Object &&
                        rel.TryGetProperty("imageUri", out var iu) &&
                        iu.ValueKind == JsonValueKind.String)
                    {
                        var s = iu.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            Add($"rocks[{ri}].imageUri", s);
                    }

                    ri++;
                }
            }

            if (p.FieldCatalogEntries is { ValueKind: JsonValueKind.Array } catArr)
            {
                var fi = 0;
                foreach (var fel in catArr.EnumerateArray())
                {
                    if (fel.ValueKind == JsonValueKind.Object &&
                        fel.TryGetProperty("photoReference", out var pr) &&
                        pr.ValueKind == JsonValueKind.String)
                    {
                        var s = pr.GetString();
                        if (!string.IsNullOrWhiteSpace(s) && IsProbableAssetPathOrUri(s))
                            Add($"fieldCatalogEntries[{fi}].photoReference", s);
                    }

                    fi++;
                }
            }

            if (p.VectorLines is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } vl)
                HarvestAssetStrings(rows, seen, cave, src, "vectorLines", vl, "", 0, 10);
        }

        return rows;
    }

    private static bool ExtensionKeySuggestsNestedMapAssets(string key) =>
        key.Contains("cartograph", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("raster", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("lidar", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("basemap", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("ortho", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("wms", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("geojson", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("kml", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("layer", StringComparison.OrdinalIgnoreCase) && key.Contains("map", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeMapRelatedKey(string key) =>
        key.Contains("cartograph", StringComparison.OrdinalIgnoreCase) ||
        (key.Contains("map", StringComparison.OrdinalIgnoreCase) && key.Contains("uri", StringComparison.OrdinalIgnoreCase)) ||
        key.Contains("raster", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("tile", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("lidar", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("sketch", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("symbol", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("overlay", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("mesh", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("MapUri", StringComparison.OrdinalIgnoreCase) ||
        key.EndsWith("mapUrl", StringComparison.OrdinalIgnoreCase) ||
        (key.Contains("image", StringComparison.OrdinalIgnoreCase) &&
         (key.Contains("map", StringComparison.OrdinalIgnoreCase) ||
          key.Contains("plan", StringComparison.OrdinalIgnoreCase) ||
          key.Contains("floor", StringComparison.OrdinalIgnoreCase) ||
          key.Contains("back", StringComparison.OrdinalIgnoreCase)));

    private const int MaxJsonDepth = 8;

    private static void CollectFromJsonValue(
        List<MapAssetRow> rows,
        HashSet<string> seen,
        string cave,
        string src,
        string prefix,
        JsonElement el,
        int depth)
    {
        if (depth > MaxJsonDepth)
            return;

        void Add(string cat, string? v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return;
            var t = v.Trim();
            if (!seen.Add($"{cat}|{t}"))
                return;
            rows.Add(new MapAssetRow(cave, cat, t, src));
        }

        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                Add(prefix, el.GetString());
                return;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var x in el.EnumerateArray())
                {
                    if (x.ValueKind == JsonValueKind.String)
                        Add($"{prefix}[{i}]", x.GetString());
                    else if (x.ValueKind == JsonValueKind.Object)
                        foreach (var p in x.EnumerateObject())
                            CollectFromJsonValue(rows, seen, cave, src, $"{prefix}[{i}].{p.Name}", p.Value, depth + 1);
                    i++;
                }

                return;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String &&
                        PropertyNameSuggestsUriOrAssetPath(p.Name))
                        Add($"{prefix}.{p.Name}", p.Value.GetString());
                    else
                        CollectFromJsonValue(rows, seen, cave, src, $"{prefix}.{p.Name}", p.Value, depth + 1);
                }

                return;
        }
    }

    private static bool PropertyNameSuggestsUriOrAssetPath(string name) =>
        name.Contains("uri", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("image", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("thumb", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("photo", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("file", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("src", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("asset", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("icon", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("overlay", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("attachment", StringComparison.OrdinalIgnoreCase);

    private static void HarvestAssetStrings(
        List<MapAssetRow> rows,
        HashSet<string> seen,
        string cave,
        string src,
        string rootKey,
        JsonElement el,
        string pathSuffix,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth)
            return;

        void Add(string cat, string? v)
        {
            if (string.IsNullOrWhiteSpace(v) || !IsProbableAssetPathOrUri(v))
                return;
            var t = v.Trim();
            if (!seen.Add($"{cat}|{t}"))
                return;
            rows.Add(new MapAssetRow(cave, cat, t, src));
        }

        var categoryBase = string.IsNullOrEmpty(pathSuffix) ? rootKey : $"{rootKey}.{pathSuffix}";

        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                Add(categoryBase, el.GetString());
                return;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var x in el.EnumerateArray())
                {
                    var next = string.IsNullOrEmpty(pathSuffix) ? $"[{i}]" : $"{pathSuffix}[{i}]";
                    HarvestAssetStrings(rows, seen, cave, src, rootKey, x, next, depth + 1, maxDepth);
                    i++;
                }

                return;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    var next = string.IsNullOrEmpty(pathSuffix) ? p.Name : $"{pathSuffix}.{p.Name}";
                    HarvestAssetStrings(rows, seen, cave, src, rootKey, p.Value, next, depth + 1, maxDepth);
                }

                return;
            default:
                return;
        }
    }

    private static bool IsProbableAssetPathOrUri(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        var t = s.Trim();
        if (t.Length < 4)
            return false;

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Path.IsPathRooted(t))
            return true;

        if (t.IndexOf('/') < 0 && t.IndexOf('\\') < 0)
            return false;

        var lower = t.Replace('\\', '/').ToLowerInvariant();
        if (lower.Contains("maps/", StringComparison.Ordinal) ||
            lower.Contains("/maps/", StringComparison.Ordinal) ||
            lower.Contains("export_assets/", StringComparison.Ordinal) ||
            lower.Contains("/export_assets/", StringComparison.Ordinal) ||
            lower.Contains("photos/", StringComparison.Ordinal) ||
            lower.Contains("/photos/", StringComparison.Ordinal) ||
            lower.Contains("cartograph", StringComparison.Ordinal) ||
            lower.Contains("raster", StringComparison.Ordinal) ||
            lower.Contains("lidar", StringComparison.Ordinal) ||
            lower.Contains("tile", StringComparison.Ordinal))
            return true;

        var ext = Path.GetExtension(t).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".tif" or ".tiff" or ".bmp" or ".svg"
            or ".obj" or ".mtl" or ".geojson" or ".kml" or ".json" or ".zip" or ".dem" or ".mbtiles" or ".las" or ".laz";
    }

    private static string? JsonString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
