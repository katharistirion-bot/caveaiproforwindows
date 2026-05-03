using System.Globalization;

namespace CaveAiProForWindows.Models;

/// <summary>One row in the backup ZIP entry list (photos, data.json, maps, …).</summary>
public sealed class ZipArchiveEntryItem
{
    public ZipArchiveEntryItem(string archiveRelativePath, long uncompressedSize, bool isDirectory)
    {
        ArchiveRelativePath = archiveRelativePath;
        UncompressedSize = uncompressedSize;
        IsDirectory = isDirectory;
    }

    /// <summary>Path inside the archive using “/”.</summary>
    public string ArchiveRelativePath { get; }

    public long UncompressedSize { get; }

    public bool IsDirectory { get; }

    public string SizeDisplay =>
        IsDirectory ? "—" : UncompressedSize.ToString("N0", CultureInfo.CurrentCulture);
}
