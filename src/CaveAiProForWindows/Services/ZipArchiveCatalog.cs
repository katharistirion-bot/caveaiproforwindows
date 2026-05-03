using System.IO;
using System.IO.Compression;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Lists all entries in a CaveAI backup ZIP (photos, manifests, future maps/ and library files).</summary>
public static class ZipArchiveCatalog
{
    /// <summary>Ordered file/folder rows for interactive UI (double-click open, extract one, …).</summary>
    public static IReadOnlyList<ZipArchiveEntryItem> GetEntryItems(string zipPath, int maxItems = 800)
    {
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var rows = zip.Entries
            .Select(e =>
            {
                var rel = e.FullName.Replace('\\', '/').TrimEnd('/');
                var isDir = e.FullName.EndsWith('/') || string.IsNullOrEmpty(e.Name);
                return (rel, e.Length, isDir);
            })
            .Where(t => !string.IsNullOrEmpty(t.rel))
            .Where(t => !t.rel.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(t.rel))
            .OrderBy(t => t.rel, StringComparer.OrdinalIgnoreCase)
            .Take(maxItems)
            .Select(t => new ZipArchiveEntryItem(t.rel, t.Length, t.isDir))
            .ToList();
        return rows;
    }

    public static string BuildListing(string zipPath, int maxLines = 400)
    {
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
        var rows = zip.Entries
            .Select(e => (Path: e.FullName.Replace('\\', '/').TrimEnd('/'), e.Length))
            .Where(t => !string.IsNullOrEmpty(t.Path))
            .OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{rows.Count} entr(y/ies). Paths use “/” as in the archive.");
        sb.AppendLine();
        var n = 0;
        foreach (var (path, size) in rows)
        {
            if (n++ >= maxLines)
            {
                sb.AppendLine($"... ({rows.Count - maxLines} more lines truncated)");
                break;
            }

            sb.AppendLine($"{size,10}  {path}");
        }

        return sb.ToString();
    }
}
