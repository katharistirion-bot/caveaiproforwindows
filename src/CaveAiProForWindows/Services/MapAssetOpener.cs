using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CaveAiProForWindows.Services;

/// <summary>Opens map asset URIs: https, local files, or paths inside a backup ZIP (extract to temp).</summary>
public static class MapAssetOpener
{
    /// <summary>Tries to open the asset. Returns error message on failure, null on success.</summary>
    public static string? TryOpen(string? uriOrPath, string? zipPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return "Empty path.";

        var raw = uriOrPath.Trim();

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (MapHttpRasterCache.TryEnsureCachedFile(raw, out var cached, out _))
            {
                Process.Start(new ProcessStartInfo(cached!) { UseShellExecute = true });
                return null;
            }

            Process.Start(new ProcessStartInfo(raw) { UseShellExecute = true });
            return null;
        }

        if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var local = new Uri(raw).LocalPath;
                if (File.Exists(local))
                {
                    Process.Start(new ProcessStartInfo(local) { UseShellExecute = true });
                    return null;
                }

                return "Local file from URI does not exist on this PC (Android path?).";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        if (Path.IsPathRooted(raw) && File.Exists(raw))
        {
            Process.Start(new ProcessStartInfo(raw) { UseShellExecute = true });
            return null;
        }

        if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
        {
            var err = TryExtractFromZipAndOpen(zipPath, raw, out _);
            if (err == null)
                return null;
            return err;
        }

        return "Cannot open: not a web URL, file not found on disk, and no .zip backup is open for embedded paths.";
    }

    /// <summary>
    /// Ensures a file exists on disk: local path, optional path relative to a .json folder, http(s) raster cache,
    /// or extract from <paramref name="zipPath"/>.
    /// </summary>
    /// <param name="resolveRelativeToDirectory">Directory containing the project .json (sibling files for relative paths in backups).</param>
    public static bool TryEnsureLocalFilePath(
        string? uriOrPath,
        string? zipPath,
        out string? localPath,
        out string? errorMessage,
        string? resolveRelativeToDirectory = null)
    {
        localPath = null;
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(uriOrPath))
        {
            errorMessage = "Empty path.";
            return false;
        }

        var raw = uriOrPath.Trim();
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (MapHttpRasterCache.TryEnsureCachedFile(raw, out localPath, out var dlErr))
                return true;
            errorMessage = MapHttpRasterCache.IsHttpRasterCacheCandidate(raw)
                ? dlErr ?? "Download failed."
                : "Web URL — use Open to launch in the browser for non-raster links.";
            return false;
        }

        if (raw.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Android content:// URI cannot be resolved on Windows.";
            return false;
        }

        if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var local = new Uri(raw).LocalPath;
                if (File.Exists(local))
                {
                    localPath = local;
                    return true;
                }

                errorMessage = "Local file from URI does not exist on this PC.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        if (Path.IsPathRooted(raw) && File.Exists(raw))
        {
            localPath = raw;
            return true;
        }

        if (TryResolveRelativeToJsonFolder(raw, resolveRelativeToDirectory, out var sibling))
        {
            localPath = sibling;
            return true;
        }

        if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
        {
            errorMessage =
                "Not found on this PC. If the path is inside a backup .zip, open that .zip here; if it is relative to an exported .json, keep the map file in the same folder layout next to the .json.";
            return false;
        }

        var extractErr = TryExtractFromZipToTemp(zipPath, raw, out var dest);
        if (extractErr != null)
        {
            errorMessage = extractErr;
            return false;
        }

        localPath = dest;
        return true;
    }

    private static bool TryResolveRelativeToJsonFolder(string raw, string? baseDir, out string? fullPath)
    {
        fullPath = null;
        if (string.IsNullOrWhiteSpace(baseDir))
            return false;
        try
        {
            var root = Path.GetFullPath(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!Directory.Exists(root))
                return false;
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(root, raw.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;
            if (File.Exists(candidate))
            {
                fullPath = candidate;
                return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    /// <summary>Finds the archive entry path (slashes) for a hint, or null.</summary>
    public static string? TryFindArchiveRelativePath(string zipPath, string hint)
    {
        var norm = hint.Replace('\\', '/').Trim().TrimStart('/');
        if (string.IsNullOrEmpty(norm) || norm.Contains("..", StringComparison.Ordinal))
            return null;

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        string? MatchEntry(string pathSlash)
        {
            var p = pathSlash.Replace('\\', '/').TrimEnd('/');
            var e = zip.Entries.FirstOrDefault(x =>
                string.Equals(x.FullName.Replace('\\', '/').TrimEnd('/'), p, StringComparison.OrdinalIgnoreCase));
            return e?.FullName.Replace('\\', '/');
        }

        var entryName = MatchEntry(norm);
        entryName ??= MatchEntry(norm.TrimStart('/'));

        if (entryName == null)
        {
            var leaf = Path.GetFileName(norm.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(leaf))
            {
                entryName = zip.Entries
                    .Select(e => e.FullName.Replace('\\', '/'))
                    .FirstOrDefault(full =>
                        !string.IsNullOrEmpty(full) &&
                        !full.EndsWith("/", StringComparison.Ordinal) &&
                        full.EndsWith("/" + leaf, StringComparison.OrdinalIgnoreCase));
            }
        }

        return string.IsNullOrEmpty(entryName) ? null : entryName;
    }

    /// <summary>Extracts a single archive entry to a user-chosen file path.</summary>
    public static string? TryExtractEntryToFile(string zipPath, string hint, string destinationFilePath)
    {
        var entry = TryFindArchiveRelativePath(zipPath, hint);
        if (string.IsNullOrEmpty(entry))
            return $"Not found in ZIP: {hint}";
        try
        {
            ZipEntryExtractor.ExtractFile(zipPath, entry, destinationFilePath);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? TryExtractFromZipAndOpen(string zipPath, string hint, out string? extractedPath)
    {
        var err = TryExtractFromZipToTemp(zipPath, hint, out var dest);
        extractedPath = dest;
        if (err != null)
            return err;
        Process.Start(new ProcessStartInfo(dest!) { UseShellExecute = true });
        return null;
    }

    private static string? TryExtractFromZipToTemp(string zipPath, string hint, out string? extractedPath)
    {
        extractedPath = null;
        var entryName = TryFindArchiveRelativePath(zipPath, hint);
        if (string.IsNullOrEmpty(entryName))
            return $"Not found in ZIP: {hint}";

        var session = Path.Combine(Path.GetTempPath(), "CaveAiProWindows", "maps", Guid.NewGuid().ToString("N"));
        var dest = CombineSafe(session, entryName);
        ZipEntryExtractor.ExtractFile(zipPath, entryName, dest);
        extractedPath = dest;
        return null;
    }

    private static string CombineSafe(string tempRoot, string archiveSlashPath)
    {
        var parts = archiveSlashPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<string> { tempRoot };
        segments.AddRange(parts);
        return Path.Combine(segments.ToArray());
    }
}
