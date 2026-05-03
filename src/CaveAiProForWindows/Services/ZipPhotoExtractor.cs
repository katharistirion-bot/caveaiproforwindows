using System.IO;
using System.IO.Compression;

namespace CaveAiProForWindows.Services;

public static class ZipPhotoExtractor
{
    /// <summary>Extracts <c>photos/**</c> entries from a CaveAI backup ZIP to <paramref name="destinationFolder"/>.</summary>
    /// <returns>Number of files written.</returns>
    public static int ExtractPhotos(string zipPath, string destinationFolder, Action<string>? logLine = null)
    {
        Directory.CreateDirectory(destinationFolder);
        var written = 0;
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var full = entry.FullName.Replace('\\', '/');
            if (!full.StartsWith("photos/", StringComparison.OrdinalIgnoreCase)) continue;

            var rel = full["photos/".Length..].TrimStart('/');
            if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal)) continue;

            var destPath = Path.Combine(destinationFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
                Directory.CreateDirectory(destDir);

            logLine?.Invoke(rel);
            using var input = entry.Open();
            using var output = File.Create(destPath);
            input.CopyTo(output);
            written++;
        }

        return written;
    }
}
