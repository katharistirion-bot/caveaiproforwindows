using System.IO;
using System.IO.Compression;

namespace CaveAiProForWindows.Services;

/// <summary>Extracts a single file from a CaveAI backup ZIP (same path rules as <see cref="ZipFullExtractor"/>).</summary>
public static class ZipEntryExtractor
{
    /// <param name="archiveRelativePathSlash">Entry path with “/” (no leading “/”).</param>
    public static void ExtractFile(string zipPath, string archiveRelativePathSlash, string destinationFilePath)
    {
        var norm = archiveRelativePathSlash.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrEmpty(norm) || norm.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(norm))
            throw new IOException("Invalid archive path.");

        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/').TrimEnd('/'), norm, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
            throw new FileNotFoundException("Entry not found in ZIP.", archiveRelativePathSlash);
        if (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
            throw new InvalidOperationException("Not a file entry.");

        var parent = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        using var input = entry.Open();
        using var output = File.Create(destinationFilePath);
        input.CopyTo(output);
    }
}
