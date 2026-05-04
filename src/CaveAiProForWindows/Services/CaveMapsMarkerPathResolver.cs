using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Resolves on-disk map rasters (PNG, TIFF, …) when paths in JSON are relative to a backup tree that uses
/// <c>_CaveAI_folder_marker.txt</c> under <c>maps/plan/</c>, <c>maps/section/</c>, etc. (same folder as the raster files).
/// </summary>
public static class CaveMapsMarkerPathResolver
{
    public const string FolderMarkerFileName = "_CaveAI_folder_marker.txt";

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> MarkerParentCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Clears cached marker-folder scans (e.g. after closing workspace).</summary>
    public static void ClearCache() => MarkerParentCache.Clear();

    /// <summary>
    /// Yields absolute file paths to try for <paramref name="rawPathFromJson"/> (e.g. <c>maps/plan/_survey_vector_plan_1.png</c>
    /// or <c>0001_name.tif</c>). Uses directory of <c>data.json</c>, ZIP parent directory, and ancestor directories so a
    /// recursive <c>_CaveAI_folder_marker.txt</c> search can find <c>maps/</c> even when the JSON lives in a subfolder.
    /// </summary>
    public static IEnumerable<string> EnumerateAbsoluteCandidates(
        string rawPathFromJson,
        CaveProjectDocument project,
        string? zipPath,
        string? jsonSidecarDirectory)
    {
        if (string.IsNullOrWhiteSpace(rawPathFromJson))
            yield break;

        var raw = rawPathFromJson.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (raw.Contains("..", StringComparison.Ordinal))
            yield break;

        if (!ShouldResolveWithMarkers(raw))
            yield break;

        var rel = raw.Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var leaf = Path.GetFileName(rel);

        foreach (var root in BuildFilesystemBackupRoots(project, zipPath, jsonSidecarDirectory))
        {
            foreach (var candidate in EnumerateCandidatesUnderRoot(rel, leaf, root))
                yield return candidate;
        }
    }

    private static bool ShouldResolveWithMarkers(string raw)
    {
        var norm = raw.Replace('\\', '/').Trim();
        if (norm.StartsWith("export_assets/", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/export_assets/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (norm.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/maps/", StringComparison.OrdinalIgnoreCase))
            return true;

        return StandaloneMapFileSupport.HasStandaloneMapExtension(raw);
    }

    /// <summary>Ordered distinct directories under which we search for markers and combine relative paths.</summary>
    private static List<string> BuildFilesystemBackupRoots(
        CaveProjectDocument project,
        string? zipPath,
        string? jsonSidecarDirectory)
    {
        var ordered = new List<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
                return;
            try
            {
                var full = Path.GetFullPath(dir.Trim());
                if (!Directory.Exists(full))
                    return;
                if (set.Add(full))
                    ordered.Add(full);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapsMarker] WARNING: invalid backup root '{dir}': {ex.Message}");
            }
        }

        Add(jsonSidecarDirectory);

        var zipDir = ZipParentDirectory(zipPath);
        Add(zipDir);

        var lf = project.LoadedFromFile;
        if (!string.IsNullOrWhiteSpace(lf))
        {
            try
            {
                var full = Path.GetFullPath(lf.Trim());
                if (File.Exists(full))
                    Add(Path.GetDirectoryName(full));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapsMarker] WARNING: LoadedFromFile path error: {ex.Message}");
            }
        }

        var ascend = jsonSidecarDirectory;
        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(ascend); depth++)
        {
            try
            {
                ascend = Path.GetDirectoryName(Path.GetFullPath(ascend));
                if (string.IsNullOrEmpty(ascend))
                    break;
                Add(ascend);
            }
            catch
            {
                break;
            }
        }

        return ordered;
    }

    private static string? ZipParentDirectory(string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(zipPath))
            return null;
        try
        {
            var full = Path.GetFullPath(zipPath.Trim());
            if (!File.Exists(full) || !full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null;
            return Path.GetDirectoryName(full);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidatesUnderRoot(string rel, string leaf, string backupRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? TryFull(string combined)
        {
            try
            {
                return Path.GetFullPath(combined);
            }
            catch
            {
                return null;
            }
        }

        var direct = TryFull(Path.Combine(backupRoot, rel));
        if (direct != null && seen.Add(direct))
        {
            Debug.WriteLine($"[MapsMarker] candidate (root+relative): {direct}");
            yield return direct;
        }

        var markerParents = SafeGetMarkerParents(backupRoot);

        foreach (var markerDir in markerParents)
        {
            var byLeaf = TryFull(Path.Combine(markerDir, leaf));
            if (byLeaf != null && seen.Add(byLeaf))
            {
                Debug.WriteLine($"[MapsMarker] candidate (markerDir+leaf): {byLeaf}");
                yield return byLeaf;
            }

            if (!string.Equals(rel, leaf, StringComparison.OrdinalIgnoreCase))
            {
                var byRel = TryFull(Path.Combine(markerDir, rel));
                if (byRel != null && seen.Add(byRel))
                {
                    Debug.WriteLine($"[MapsMarker] candidate (markerDir+relative): {byRel}");
                    yield return byRel;
                }
            }
        }
    }

    private static IReadOnlyList<string> SafeGetMarkerParents(string backupRoot)
    {
        try
        {
            return GetMarkerParentDirectoriesCached(backupRoot);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapsMarker] WARNING: marker scan failed under {backupRoot}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> GetMarkerParentDirectoriesCached(string backupRoot)
    {
        try
        {
            var key = Path.GetFullPath(backupRoot);
            return MarkerParentCache.GetOrAdd(key, static k => ScanMarkerParents(k));
        }
        catch
        {
            return ScanMarkerParents(backupRoot);
        }
    }

    private static string[] ScanMarkerParents(string backupRoot)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(backupRoot))
            {
                Debug.WriteLine($"[MapsMarker] WARNING: backup root is not an existing directory: {backupRoot}");
                return Array.Empty<string>();
            }

            foreach (var marker in Directory.EnumerateFiles(backupRoot, FolderMarkerFileName, SearchOption.AllDirectories))
            {
                try
                {
                    var dir = Path.GetDirectoryName(marker);
                    if (!string.IsNullOrEmpty(dir))
                        list.Add(Path.GetFullPath(dir));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MapsMarker] WARNING: skip marker file '{marker}': {ex.Message}");
                }
            }

            Debug.WriteLine($"[MapsMarker] under '{backupRoot}' found {list.Count} folder(s) containing {FolderMarkerFileName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapsMarker] WARNING: recursive marker search failed for '{backupRoot}': {ex.Message}");
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
