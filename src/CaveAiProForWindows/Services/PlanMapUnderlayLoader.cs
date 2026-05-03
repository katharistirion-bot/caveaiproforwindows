using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Loads a raster image from the first resolvable map asset for the project (same discovery as the Maps tab),
/// stretched under plan/section vectors. Not georeferenced — visual context only.
/// </summary>
public static class PlanMapUnderlayLoader
{
    /// <param name="extraMapRows">Optional UI rows (e.g. standalone files from <see cref="MainViewModel.MapAssetRows"/>).</param>
    /// <param name="mapInventoryRows">Optional rows from Android <c>map_inventory.json</c> (same paths as backup, helps when ExtensionData misses nested URIs).</param>
    public static BitmapSource? TryLoadRasterUnderlay(
        CaveProjectDocument? project,
        string? zipPath,
        IEnumerable? extraMapRows = null,
        IEnumerable? mapInventoryRows = null)
    {
        if (project == null)
            return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<MapAssetRow>();

        void AddRow(MapAssetRow r)
        {
            var key = r.Category + "|" + r.UriOrPath;
            if (!seen.Add(key))
                return;
            candidates.Add(r);
        }

        foreach (var r in MapAssetsCollector.Collect(new[] { project }, includeTraverseShotMedia: false))
            AddRow(r);

        if (mapInventoryRows != null)
        {
            var caveName = string.IsNullOrWhiteSpace(project.Name) ? "(unnamed)" : project.Name.Trim();
            var src = project.LoadedFromFile ?? zipPath;
            foreach (var o in mapInventoryRows)
            {
                if (o is not MapInventoryRow inv)
                    continue;
                if (!string.Equals(inv.ProjectName.Trim(), caveName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = (inv.PathInBackupOrUrl ?? "").Trim();
                if (string.IsNullOrEmpty(path))
                    continue;
                AddRow(new MapAssetRow(caveName, $"map_inventory.{inv.Slot}", path, src));
            }
        }

        if (extraMapRows != null)
        {
            foreach (var o in extraMapRows)
            {
                if (o is not MapAssetRow r)
                    continue;
                if (!IsRowForProject(r, project))
                    continue;
                AddRow(r);
            }
        }

        var ordered = candidates
            .Where(IsPlanUnderlayRasterCandidate)
            .OrderByDescending(static r => ScoreCategory(r.Category))
            .ThenBy(static r => PublicLibraryCartographyIndex(r.Category))
            .ThenBy(static r => MapAssetSortKeys.OrdinalBracketKey(r.Category), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static r => r.UriOrPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var jsonSidecar = JsonSidecarDirectory(project);
        foreach (var row in ordered)
        {
            if (!MapAssetOpener.TryEnsureLocalFilePath(row.UriOrPath, zipPath, out var local, out _, jsonSidecar))
                continue;
            if (string.IsNullOrEmpty(local) || !File.Exists(local))
                continue;
            var bmp = RasterImageDecoder.TryLoadBitmap(local);
            if (bmp != null)
                return bmp;
        }

        return null;
    }

    private static bool IsRowForProject(MapAssetRow r, CaveProjectDocument project)
    {
        if (string.Equals(r.ProjectName, "(standalone)", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(r.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreCategory(string category)
    {
        var c = category.ToLowerInvariant();
        if (c.Contains("surfacelidar", StringComparison.Ordinal))
            return 4;
        if (c.Contains("map_inventory", StringComparison.Ordinal))
            return 3;
        if (c.Contains("publiclibrarycartography", StringComparison.Ordinal) ||
            c.Contains("cartographytls", StringComparison.Ordinal))
            return 3;
        if (c.Contains("sketch", StringComparison.Ordinal))
            return 2;
        if (c.Contains("raster", StringComparison.Ordinal) || c.Contains("mesh", StringComparison.Ordinal))
            return 1;
        return 0;
    }

    /// <summary>Library list order: publicLibraryCartographyUris[0] before [1]; non-library rows sort after.</summary>
    private static int PublicLibraryCartographyIndex(string category)
    {
        var m = Regex.Match(category ?? "", @"publicLibraryCartographyUris\[(\d+)\]", RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : int.MaxValue;
    }

    /// <summary>
    /// Plan/Section underlay must not use traverse shot photos (<c>photos/</c>) — they are not map rasters and look “wrong”.
    /// </summary>
    private static bool IsPlanUnderlayRasterCandidate(MapAssetRow r)
    {
        var cat = r.Category ?? "";
        var path = (r.UriOrPath ?? "").Trim();
        var pathNorm = path.Replace('\\', '/');
        if (pathNorm.Contains("..", StringComparison.Ordinal))
            return false;

        if (cat.Contains("shots[", StringComparison.OrdinalIgnoreCase) && cat.Contains("photos", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cat.Contains("audiomemo", StringComparison.OrdinalIgnoreCase) ||
            (cat.Contains("shots[", StringComparison.OrdinalIgnoreCase) && cat.Contains("audio", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (pathNorm.StartsWith("photos/", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cat.Contains("rocks[", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cat.Contains("fieldcatalog", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cat.Contains("mapsymbol", StringComparison.OrdinalIgnoreCase))
            return false;

        var q = StripQuery(path);
        if (StandaloneMapFileSupport.HasStandaloneMapExtension(q))
            return true;
        if (MapHttpRasterCache.IsHttpRasterCacheCandidate(path))
            return true;
        if (pathNorm.StartsWith("export_assets/", StringComparison.OrdinalIgnoreCase))
            return true;
        // Android ZIP backups often store rasters under maps/ (relative to archive root).
        return pathNorm.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
               pathNorm.Contains("/maps/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? JsonSidecarDirectory(CaveProjectDocument p)
    {
        var lf = p.LoadedFromFile;
        if (string.IsNullOrEmpty(lf) || !lf.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var full = Path.GetFullPath(lf);
            if (!File.Exists(full))
                return null;
            return Path.GetDirectoryName(full);
        }
        catch
        {
            return null;
        }
    }

    private static string StripQuery(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return "";
        var t = uriOrPath.Trim();
        var q = t.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? t[..q] : t;
    }

}
