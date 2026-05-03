using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Maps JSON field paths (from <see cref="MapAssetsCollector"/>) to user-facing labels for the Maps grid.
/// </summary>
public static class MapAssetKindClassifier
{
    public static string GetMapTypeLabel(string projectName, string category, string uriOrPath)
    {
        if (string.Equals(projectName, "(standalone)", StringComparison.OrdinalIgnoreCase))
            return "Standalone map file";

        var c = (category ?? "").ToLowerInvariant();
        var path = (uriOrPath ?? "").Trim();
        var pathLower = path.Replace('\\', '/').ToLowerInvariant();

        if (c.Contains("publiclibrarycartography", StringComparison.Ordinal))
            return "Library map (raster)";

        if (c.Contains("surfacelidar", StringComparison.Ordinal) ||
            (c.Contains("surface", StringComparison.Ordinal) && c.Contains("lidar", StringComparison.Ordinal)))
            return "Surface LiDAR / DSM raster";

        if (c.Contains("cartographytls", StringComparison.Ordinal) ||
            c.Contains("tlsmesh", StringComparison.Ordinal) ||
            pathLower.EndsWith(".obj", StringComparison.Ordinal) ||
            pathLower.EndsWith(".mtl", StringComparison.Ordinal))
            return "3D survey mesh (TLS / OBJ)";

        if (c.Contains("mapsymbol", StringComparison.Ordinal))
            return "Map symbol artwork";

        if (c.Contains("sectionsketch", StringComparison.Ordinal))
            return "Section sketch (drawing)";

        if (c.Contains("sketch", StringComparison.Ordinal))
            return "Plan / wall sketch (drawing)";

        if (c.Contains("vectorline", StringComparison.Ordinal))
            return "Vector overlay (line asset)";

        if (c.Contains("trackpoint", StringComparison.Ordinal))
            return "Surface GPS track (data)";

        if (c.Contains("shots[", StringComparison.Ordinal) && c.Contains("photos", StringComparison.Ordinal))
            return "Shot photo (traverse)";

        if (c.Contains("audiomemo", StringComparison.Ordinal) ||
            (c.Contains("shots[", StringComparison.Ordinal) && c.Contains("audio", StringComparison.Ordinal)))
            return "Shot voice memo";

        if (c.Contains("rocks[", StringComparison.Ordinal) || c.StartsWith("rocks[", StringComparison.Ordinal))
            return "Geo/Bio rock photo";

        if (c.Contains("fieldcatalog", StringComparison.Ordinal))
            return "Field catalog photo";

        if (pathLower.StartsWith("http://", StringComparison.Ordinal) ||
            pathLower.StartsWith("https://", StringComparison.Ordinal))
        {
            if (StandaloneMapFileSupport.HasStandaloneMapExtension(path) || MapHttpRasterCache.IsHttpRasterCacheCandidate(path))
                return "Web map / raster URL";
            return "Web link (map-related)";
        }

        if (pathLower.StartsWith("export_assets/", StringComparison.Ordinal))
            return "Bundled map media (ZIP)";

        if (pathLower.StartsWith("photos/", StringComparison.Ordinal))
            return "Shot photo file (ZIP)";

        if (c.Contains("cartograph", StringComparison.Ordinal) || c.Contains("raster", StringComparison.Ordinal) ||
            c.Contains("basemap", StringComparison.Ordinal) || c.Contains("ortho", StringComparison.Ordinal) ||
            c.Contains("tile", StringComparison.Ordinal) || c.Contains("overlay", StringComparison.Ordinal))
            return "Cartography / raster asset";

        var ext = Path.GetExtension(path.Split('?', 2)[0]).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".tif" or ".tiff" or ".bmp" or ".gif")
            return "Raster image";

        return "Map-related file";
    }

    /// <summary>
    /// One explicit label per row (matches Android map surfaces where applicable: Plan, Section, 3D, long profile, X-ray, …).
    /// </summary>
    public static string GetDetailLabel(string projectName, string category, string uriOrPath)
    {
        if (string.Equals(projectName, "(standalone)", StringComparison.OrdinalIgnoreCase))
        {
            var leaf = SafeFileName(uriOrPath);
            return string.IsNullOrEmpty(leaf) ? "Standalone map file" : $"Standalone map: {leaf}";
        }

        var c = category ?? "";
        var path = (uriOrPath ?? "").Trim();
        var pathLower = path.Replace('\\', '/').ToLowerInvariant();

        if (TryMatch(c, @"publicLibraryCartographyUris\[(\d+)\]", out var libIdx))
        {
            var n = ParseIndex(libIdx) + 1;
            return
                $"Library cartography raster #{n} — Android “Public library” list (Plan / X-ray / 2-tone map tabs)";
        }

        if (string.Equals(c, "cartographyTlsMeshObjUri", StringComparison.OrdinalIgnoreCase) ||
            c.EndsWith(".cartographyTlsMeshObjUri", StringComparison.OrdinalIgnoreCase))
        {
            return "3D TLS / photogrammetry mesh (OBJ) — same survey frame as LRUD; Android MODEL (3D) tab";
        }

        if (c.Equals("surfaceLidarRaster.imageUri", StringComparison.OrdinalIgnoreCase) ||
            c.EndsWith("surfaceLidarRaster.imageUri", StringComparison.OrdinalIgnoreCase))
        {
            return "Surface LiDAR / DSM / hillshade image — georeferenced Plan underlay (Android Surface map)";
        }

        if (c.StartsWith("surfaceLidarRaster.", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("imageUri", StringComparison.OrdinalIgnoreCase))
        {
            return "Surface LiDAR / DSM raster image — Plan underlay";
        }

        if (c.StartsWith("surfaceLidarRaster", StringComparison.OrdinalIgnoreCase))
        {
            var tail = c.Length > "surfaceLidarRaster".Length ? c["surfaceLidarRaster".Length..].TrimStart('.') : "";
            return string.IsNullOrEmpty(tail)
                ? "Surface LiDAR / DSM overlay (raster or settings)"
                : $"Surface LiDAR overlay field “{tail}”";
        }

        if (TryMatch(c, @"shots\[(\d+)\]\.photos\[(\d+)\]", out var si, out var pi))
        {
            var shot = ParseIndex(si) + 1;
            var photo = ParseIndex(pi) + 1;
            return
                $"Traverse shot #{shot}, field photo #{photo} — station photos (not Plan map tab geometry)";
        }

        if (TryMatch(c, @"shots\[(\d+)\]\.audioMemoUri", out var asi))
        {
            var shot = ParseIndex(asi) + 1;
            return $"Traverse shot #{shot} — voice memo (audio, not a map image)";
        }

        if (TryMatch(c, @"rocks\[(\d+)\]\.imageUri", out var ri))
        {
            var rock = ParseIndex(ri) + 1;
            return $"Geo/Bio rock sample #{rock} — specimen photo";
        }

        if (TryMatch(c, @"fieldCatalogEntries\[(\d+)\]\.photoReference", out var fi))
        {
            var entry = ParseIndex(fi) + 1;
            return $"Field catalog entry #{entry} — attached photo";
        }

        if (TryMatch(c, @"mapSymbols\[(\d+)\]\.icon", out var mi))
        {
            var sym = ParseIndex(mi) + 1;
            return
                $"Map symbol #{sym} — icon asset (Plan / Section / profile / X-ray depending on symbol view mode in Android)";
        }

        if (TryMatch(c, @"mapSymbols\[(\d+)\]", out var mix))
        {
            var sym = ParseIndex(mix) + 1;
            return $"Map symbol #{sym} — nested asset path";
        }

        if (TryMatch(c, @"sketches\[(\d+)\]", out var ski))
        {
            var idx = ParseIndex(ski) + 1;
            return
                $"Wall / plan sketch polyline #{idx} — Plan, X-ray, or Plan 2-tone sketch list (Android sketches)";
        }

        if (TryMatch(c, @"sectionSketches\[(\d+)\]", out var seci))
        {
            var idx = ParseIndex(seci) + 1;
            return
                $"Section sketch #{idx} — Section or Section night wall profile (Android sectionSketches)";
        }

        if (TryMatch(c, @"vectorLines\[(\d+)\]", out var vi))
        {
            var idx = ParseIndex(vi) + 1;
            return
                $"Vector line overlay group #{idx} — Plan(0), Section(1), 3D(2), long profile(3), X-ray(4), plan 2-tone(5), section night(6) per line viewMode in JSON";
        }

        if (c.Contains("vectorLines", StringComparison.OrdinalIgnoreCase))
        {
            return $"Vector line overlay — {c} (viewMode in JSON selects Android map tab)";
        }

        if (TryMatch(c, @"trackPoints\[(\d+)\]", out var ti))
        {
            var idx = ParseIndex(ti) + 1;
            return $"Surface GPS track point #{idx} — entrance / surface breadcrumb";
        }

        if (pathLower.StartsWith("http://", StringComparison.Ordinal) || pathLower.StartsWith("https://", StringComparison.Ordinal))
        {
            var leaf = SafeFileName(path);
            return string.IsNullOrEmpty(leaf)
                ? "Web map or document URL"
                : $"Web asset: {leaf}";
        }

        if (pathLower.StartsWith("export_assets/", StringComparison.Ordinal))
        {
            var leaf = SafeFileName(path);
            return string.IsNullOrEmpty(leaf)
                ? "File inside backup ZIP under export_assets/"
                : $"ZIP export_assets: {leaf}";
        }

        if (pathLower.StartsWith("photos/", StringComparison.Ordinal))
        {
            var leaf = SafeFileName(path);
            return string.IsNullOrEmpty(leaf) ? "ZIP photos/ traverse photo" : $"ZIP photos/: {leaf}";
        }

        if (c.StartsWith("extra.", StringComparison.OrdinalIgnoreCase))
        {
            return $"Other map-related JSON field — {c}";
        }

        var type = GetMapTypeLabel(projectName, category ?? "", uriOrPath ?? "");
        return $"{type} — {c}";
    }

    private static string SafeFileName(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return "";
        try
        {
            var t = pathOrUrl.Trim().Split('?', 2)[0].Replace('\\', '/');
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(pathOrUrl.Trim(), UriKind.Absolute, out var u))
                    return Path.GetFileName(u.AbsolutePath);
            }

            return Path.GetFileName(t);
        }
        catch
        {
            return "";
        }
    }

    private static bool TryMatch(string input, string pattern, out string g1)
    {
        g1 = "";
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success || m.Groups.Count < 2)
            return false;
        g1 = m.Groups[1].Value;
        return true;
    }

    private static bool TryMatch(string input, string pattern, out string g1, out string g2)
    {
        g1 = "";
        g2 = "";
        var m = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success || m.Groups.Count < 3)
            return false;
        g1 = m.Groups[1].Value;
        g2 = m.Groups[2].Value;
        return true;
    }

    private static int ParseIndex(string s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
}
