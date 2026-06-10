using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Loads raster images from <b>all</b> resolvable map assets for the project (same discovery as the Maps tab),
/// each as its own underlay layer under plan/section vectors. When JSON omits per-map extents, each layer uses the
/// same padded fit as the vector scene (see <see cref="PlanCanvasRenderer"/>). Paths resolve next to <c>data.json</c>
/// / the open <c>.zip</c>, <c>cartography</c> siblings, or <c>maps/</c> trees with <c>_CaveAI_folder_marker.txt</c>
/// (<see cref="CaveMapsMarkerPathResolver"/>).
/// </summary>
public static class PlanMapUnderlayLoader
{
    private const int MaxRasterUnderlaysPerProject = 48;

    /// <summary>Subfolders next to the survey file searched for leaf / relative cartography rasters.</summary>
    private static readonly string[] CartographySubfolderNames =
    [
        "cartography",
        "Cartography",
        "cartography_backup",
        "Cartography_backup",
        "map_cartography",
        "rasters",
        "maps_raster",
    ];
    /// <param name="extraMapRows">Optional UI rows (e.g. standalone files from <see cref="MainViewModel.MapAssetRows"/>).</param>
    /// <param name="mapInventoryRows">Optional rows from Android <c>map_inventory.json</c> (same paths as backup, helps when ExtensionData misses nested URIs).</param>
    public static IReadOnlyList<PlanRasterUnderlay> TryLoadRasterUnderlays(
        CaveProjectDocument? project,
        string? zipPath,
        IEnumerable? extraMapRows = null,
        IEnumerable? mapInventoryRows = null)
    {
        if (project == null)
            return Array.Empty<PlanRasterUnderlay>();

        var seenRowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<MapAssetRow>();

        void AddRow(MapAssetRow r)
        {
            var key = r.Category + "|" + r.UriOrPath;
            if (!seenRowKeys.Add(key))
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

        Debug.WriteLine($"[PlanMapUnderlay] project={project.Name}, raster candidates={ordered.Count}, zip={(zipPath ?? "(none)")}");

        var jsonSidecar = JsonSidecarDirectory(project);
        var loaded = new List<PlanRasterUnderlay>();
        var loadedDiskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ordered)
        {
            if (loaded.Count >= MaxRasterUnderlaysPerProject)
                break;

            var raw = (row.UriOrPath ?? "").Trim();
            Debug.WriteLine($"[PlanMapUnderlay] row category={row.Category}, path={raw}");
            foreach (var local in ResolveUnderlayLocalCandidates(raw, zipPath, jsonSidecar, project))
            {
                Debug.WriteLine($"[PlanMapUnderlay] try load: {local}");
                if (!File.Exists(local))
                {
                    Debug.WriteLine($"[PlanMapUnderlay] WARNING: file not found: {local}");
                    continue;
                }

                string fullDisk;
                try
                {
                    fullDisk = Path.GetFullPath(local);
                }
                catch
                {
                    continue;
                }

                if (!loadedDiskPaths.Add(fullDisk))
                {
                    Debug.WriteLine($"[PlanMapUnderlay] skip duplicate disk path: {fullDisk}");
                    continue;
                }

                var bmp = RasterImageDecoder.TryLoadBitmap(local);
                if (bmp != null)
                {
                    var extent = RasterUnderlayBoundsParser.TryParse(project, row, fullDisk);
                    Debug.WriteLine(
                        extent != null
                            ? $"[PlanMapUnderlay] loaded underlay OK (with JSON extent): {fullDisk}"
                            : $"[PlanMapUnderlay] loaded underlay OK: {fullDisk}");
                    loaded.Add(new PlanRasterUnderlay
                    {
                        Bitmap = bmp,
                        ResolvedPath = fullDisk,
                        Category = row.Category,
                        WorldExtentMetres = extent,
                    });
                    break;
                }

                Debug.WriteLine($"[PlanMapUnderlay] WARNING: decode failed (corrupt/unsupported?): {local}");
                loadedDiskPaths.Remove(fullDisk);
            }
        }

        Debug.WriteLine(loaded.Count > 0
            ? $"[PlanMapUnderlay] resolved {loaded.Count} raster underlay layer(s)."
            : "[PlanMapUnderlay] no raster underlay resolved.");
        return loaded;
    }

    /// <summary>Backward-compatible: first successfully decoded raster, or null.</summary>
    public static BitmapSource? TryLoadRasterUnderlay(
        CaveProjectDocument? project,
        string? zipPath,
        IEnumerable? extraMapRows = null,
        IEnumerable? mapInventoryRows = null)
    {
        var list = TryLoadRasterUnderlays(project, zipPath, extraMapRows, mapInventoryRows);
        return list.Count > 0 ? list[0].Bitmap : null;
    }

    /// <summary>
    /// Decodes one image path/URI for a project using the same resolution rules as plan underlays (ZIP, cartography folders, maps marker).
    /// </summary>
    public static BitmapSource? TryLoadProjectImageBitmap(CaveProjectDocument project, string? zipPath, string uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return null;
        var raw = uriOrPath.Trim();
        var jsonSidecar = JsonSidecarDirectory(project);
        foreach (var local in ResolveUnderlayLocalCandidates(raw, zipPath, jsonSidecar, project))
        {
            if (!File.Exists(local))
                continue;
            var bmp = RasterImageDecoder.TryLoadBitmap(local);
            if (bmp != null)
                return bmp;
        }

        return null;
    }

    /// <summary>
    /// Yields absolute paths: MapAssetOpener resolution first, then sibling <see cref="CartographySubfolderNames"/> combinations
    /// for TIFF-like references (e.g. numbered <c>0001_site.tif</c> not embedded in JSON as full paths).
    /// </summary>
    private static IEnumerable<string> ResolveUnderlayLocalCandidates(
        string raw,
        string? zipPath,
        string? jsonSidecar,
        CaveProjectDocument project)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (MapAssetOpener.TryEnsureLocalFilePath(raw, zipPath, out var openerPath, out var openerErr, jsonSidecar) &&
            !string.IsNullOrWhiteSpace(openerPath))
        {
            string fullOpener;
            try
            {
                fullOpener = Path.GetFullPath(openerPath.Trim());
            }
            catch
            {
                fullOpener = openerPath;
            }

            if (seen.Add(fullOpener))
                yield return fullOpener;
            if (File.Exists(fullOpener))
                yield break;
            Debug.WriteLine($"[PlanMapUnderlay] MapAssetOpener path not on disk: {fullOpener} ({openerErr})");
        }
        else if (!string.IsNullOrEmpty(openerErr))
            Debug.WriteLine($"[PlanMapUnderlay] MapAssetOpener: {raw} -> {openerErr}");

        List<string>? markerList = null;
        try
        {
            markerList = CaveMapsMarkerPathResolver.EnumerateAbsoluteCandidates(raw, project, zipPath, jsonSidecar).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PlanMapUnderlay] WARNING: maps-marker resolution failed for '{raw}': {ex.Message}");
        }

        if (markerList != null)
        {
            foreach (var markerPath in markerList)
            {
                string full;
                try
                {
                    full = Path.GetFullPath(markerPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlanMapUnderlay] WARNING: maps-marker path invalid '{markerPath}': {ex.Message}");
                    continue;
                }

                if (!seen.Add(full))
                    continue;
                yield return full;
            }
        }

        if (!ShouldTryCartographyFolderHints(raw))
            yield break;

        foreach (var cart in EnumerateCartographyAbsolutePaths(raw, project, zipPath))
        {
            string full;
            try
            {
                full = Path.GetFullPath(cart);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(full))
                continue;
            yield return full;
        }
    }

    private static bool ShouldTryCartographyFolderHints(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var t = raw.Trim();
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            return false;

        var ext = Path.GetExtension(StripQuery(t)).ToLowerInvariant();
        return ext is ".tif" or ".tiff" or ".geotiff";
    }

    private static IEnumerable<string> EnumerateCartographyAbsolutePaths(string raw, CaveProjectDocument project, string? zipPath)
    {
        var norm = raw.Trim().Replace('\\', '/');
        if (norm.Contains("..", StringComparison.Ordinal))
            yield break;

        var relForCombine = norm.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in EnumerateSurveySidecarRoots(project, zipPath))
        {
            // Same folder as survey file
            yield return Path.GetFullPath(Path.Combine(root, relForCombine));

            foreach (var sub in CartographySubfolderNames)
            {
                var subDir = Path.Combine(root, sub);
                yield return Path.GetFullPath(Path.Combine(subDir, relForCombine));
            }
        }
    }

    /// <summary>Parent directories of the opened <c>.json</c> or <c>.zip</c> (survey "workspace" roots).</summary>
    private static IEnumerable<string> EnumerateSurveySidecarRoots(CaveProjectDocument project, string? zipPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddFileParent(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                var full = Path.GetFullPath(path.Trim());
                if (!File.Exists(full))
                    return;
                var dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir))
                    roots.Add(Path.GetFullPath(dir));
            }
            catch
            {
                /* ignore */
            }
        }

        AddFileParent(project.LoadedFromFile);
        AddFileParent(zipPath);

        foreach (var r in roots)
            yield return r;
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
        // Leaf TIFF names from JSON (resolved via cartography folder next to survey file).
        var ext = Path.GetExtension(q).ToLowerInvariant();
        if (ext is ".tif" or ".tiff" or ".geotiff")
        {
            if (pathNorm.IndexOf('/') < 0 && pathNorm.IndexOf('\\') < 0)
                return true;
        }

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

    /// <summary>
    /// Appends the generative AI map as the top raster underlay when enabled. Skips duplicate disk paths already loaded.
    /// </summary>
    public static IReadOnlyList<PlanRasterUnderlay> WithGenerativeUnderlay(
        CaveProjectDocument? project,
        string? zipPath,
        IReadOnlyList<PlanRasterUnderlay> baseLayers,
        bool showGenerative,
        double opacity)
    {
        if (project == null || !showGenerative)
            return baseLayers;

        BitmapSource? bitmap = null;
        var resolvedPath = "(generative session)";

        var session = GenerativeMapSessionCache.TryGet(project);
        if (session?.Bitmap != null)
        {
            bitmap = session.Bitmap;
        }
        else
        {
            var aiPath = ProjectAiAssetPersistence.TryReadAiMapRelativePath(project);
            if (!string.IsNullOrWhiteSpace(aiPath))
            {
                var src = project.LoadedFromFile ?? zipPath;
                if (!string.IsNullOrWhiteSpace(src))
                {
                    var bytes = ProjectAiAssetPersistence.TryLoadAssetBytes(src, aiPath);
                    if (bytes is { Length: > 0 })
                    {
                        try
                        {
                            using var ms = new MemoryStream(bytes);
                            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            if (decoder.Frames.Count > 0)
                                bitmap = decoder.Frames[0];
                            resolvedPath = aiPath;
                        }
                        catch
                        {
                            // ignore decode failure
                        }
                    }
                }
            }
        }

        if (bitmap == null)
            return baseLayers;

        // Drop persisted AI cartography from base list to avoid double-draw.
        var filtered = baseLayers
            .Where(u =>
                !string.Equals(u.Category, "generativeMap", StringComparison.OrdinalIgnoreCase) &&
                !IsGenerativeAssetPath(u.ResolvedPath))
            .ToList();

        filtered.Add(new PlanRasterUnderlay
        {
            Bitmap = bitmap,
            Category = "generativeMap",
            ResolvedPath = resolvedPath,
            OpacityOverride = Math.Clamp(opacity, 0.12, 0.95),
        });

        return filtered;
    }

    private static bool IsGenerativeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var norm = path.Replace('\\', '/');
        return norm.Contains("/ai_cartography.png", StringComparison.OrdinalIgnoreCase) ||
               norm.Contains("export_assets/windows/", StringComparison.OrdinalIgnoreCase) &&
               norm.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

}
