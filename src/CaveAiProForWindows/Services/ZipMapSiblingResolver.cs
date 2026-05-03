using System.IO;
using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// When the user opens an exported <c>data.json</c> without the backup <c>.zip</c>, embedded paths like <c>maps/…</c>
/// cannot resolve. If a sibling <c>.zip</c> in the same folder contains those entries, use it for map extraction.
/// </summary>
public static class ZipMapSiblingResolver
{
    /// <param name="loadedFromPath">Absolute path to the survey JSON or ZIP that produced <paramref name="project"/>.</param>
    public static string? TryResolve(string? loadedFromPath, CaveProjectDocument? project)
    {
        if (project == null || string.IsNullOrWhiteSpace(loadedFromPath))
            return null;
        if (!loadedFromPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var jsonFull = Path.GetFullPath(loadedFromPath.Trim());
            if (!File.Exists(jsonFull))
                return null;
            var dir = Path.GetDirectoryName(jsonFull);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return null;

            var hints = new List<string>();
            foreach (var row in MapAssetsCollector.Collect(new[] { project }, includeTraverseShotMedia: false))
            {
                var u = (row.UriOrPath ?? "").Trim();
                if (string.IsNullOrEmpty(u))
                    continue;
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (u.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (u.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Path.IsPathRooted(u))
                {
                    try
                    {
                        if (File.Exists(u))
                            continue;
                    }
                    catch
                    {
                        /* treat as non-resolved hint */
                    }
                }

                if (u.Contains("..", StringComparison.Ordinal))
                    continue;
                hints.Add(u);
                if (hints.Count >= 30)
                    break;
            }

            if (hints.Count == 0)
                return null;

            var zips = Directory.EnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                .Take(48)
                .ToList();

            foreach (var zip in zips)
            {
                if (!File.Exists(zip))
                    continue;
                foreach (var h in hints)
                {
                    if (MapAssetOpener.TryFindArchiveRelativePath(zip, h) != null)
                        return zip;
                }
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }
}
