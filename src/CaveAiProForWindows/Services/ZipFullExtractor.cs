using System.IO;
using System.IO.Compression;

namespace CaveAiProForWindows.Services;

/// <summary>Extracts every file from a backup ZIP preserving relative paths (photos, future maps/, library assets).</summary>
public static class ZipFullExtractor
{
    /// <returns>Number of files written (not directories).</returns>
    public static int ExtractAll(string zipPath, string destinationRoot, Action<string>? logLine = null)
    {
        Directory.CreateDirectory(destinationRoot);
        var written = 0;
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && !entry.FullName.EndsWith('/'))
                continue;

            var rel = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(rel))
                continue;
            if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                continue;

            if (entry.FullName.EndsWith('/'))
            {
                var dirOnly = Path.Combine(destinationRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dirOnly);
                continue;
            }

            var destPath = Path.Combine(destinationRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            logLine?.Invoke(rel);
            using var input = entry.Open();
            using var output = File.Create(destPath);
            input.CopyTo(output);
            written++;
        }

        return written;
    }
}
