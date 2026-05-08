using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// One discovered image in the global PHOTOS gallery (resolved bitmap + provenance metadata).
/// </summary>
public sealed class BackupPhotoEntry
{
    public BackupPhotoEntry(
        BitmapSource bitmap,
        string caption,
        string category,
        string groupKey,
        string? station,
        string sourceLabel,
        string? localPath)
    {
        Bitmap = bitmap;
        Caption = caption;
        Category = category;
        GroupKey = groupKey;
        Station = station;
        SourceLabel = sourceLabel;
        LocalPath = localPath;
    }

    public BitmapSource Bitmap { get; }
    public string Caption { get; }
    /// <summary>One of: "Survey", "Geo", "Bio", "X-Ray", "Map / Backdrop", "Other".</summary>
    public string Category { get; }
    /// <summary>Top-level group bucket (station name or category) used by the WPF CollectionView.</summary>
    public string GroupKey { get; }
    public string? Station { get; }
    public string SourceLabel { get; }
    public string? LocalPath { get; }
}

/// <summary>
/// Builds the unified set of <see cref="BackupPhotoEntry"/> rows for the PHOTOS tab — scans the loaded project's
/// JSON, all known scientific arrays, traverse shot photos, sketches, and every raster entry inside the active
/// backup ZIP. Categorises each photo and groups by station (where known) or category.
/// </summary>
public static class BackupPhotoIndexer
{
    private static readonly string[] RasterExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff",
    ];

    public static IReadOnlyList<BackupPhotoEntry> Build(CaveProjectDocument? project, string? zipPath, int maxEntries = 400)
    {
        var entries = new List<BackupPhotoEntry>();
        var seenLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenZipEntry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (project != null)
        {
            AppendScientificRecordPhotos(entries, project, zipPath, seenLocal, maxEntries);
            AppendShotPhotos(entries, project, zipPath, seenLocal, maxEntries);
            AppendXRayBackdrop(entries, project, zipPath, seenLocal, maxEntries);
        }

        if (entries.Count < maxEntries && !string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
            AppendZipImages(entries, zipPath!, seenZipEntry, maxEntries);

        return entries;
    }

    private static void AppendScientificRecordPhotos(
        List<BackupPhotoEntry> entries,
        CaveProjectDocument project,
        string? zipPath,
        HashSet<string> seenLocal,
        int maxEntries)
    {
        var records = GeoBioRecordsService.Build(project);
        foreach (var r in records)
        {
            if (entries.Count >= maxEntries)
                return;
            var category = r.Category switch
            {
                GeoBioCategory.Organism => "Bio",
                GeoBioCategory.Rock => "Geo",
                _ => "Other",
            };
            foreach (var img in ScientificImageLoader.Load(r, project, zipPath))
            {
                if (entries.Count >= maxEntries)
                    return;
                if (string.IsNullOrEmpty(img.Reference))
                    continue;
                if (!seenLocal.Add(img.Reference))
                    continue;
                var groupKey = !string.IsNullOrWhiteSpace(r.Station) ? $"Station {r.Station}" : category;
                entries.Add(new BackupPhotoEntry(
                    img.Bitmap,
                    img.Caption,
                    category,
                    groupKey,
                    r.Station,
                    r.SourceLabel,
                    null));
            }
        }
    }

    private static void AppendShotPhotos(
        List<BackupPhotoEntry> entries,
        CaveProjectDocument project,
        string? zipPath,
        HashSet<string> seenLocal,
        int maxEntries)
    {
        var jsonDir = TryResolveJsonSidecarDirectory(project);

        for (var i = 0; i < project.Shots.Count; i++)
        {
            var shot = project.Shots[i];
            var station = (shot.FromStation ?? "").Trim();
            var pi = 0;
            foreach (var photoUri in shot.Photos)
            {
                if (entries.Count >= maxEntries)
                    return;
                if (string.IsNullOrWhiteSpace(photoUri))
                {
                    pi++;
                    continue;
                }

                if (seenLocal.Contains(photoUri))
                {
                    pi++;
                    continue;
                }

                if (!MapAssetOpener.TryEnsureLocalFilePath(photoUri, zipPath, out var local, out _, jsonDir))
                {
                    pi++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                {
                    pi++;
                    continue;
                }

                if (RasterImageDecoder.TryLoadBitmap(local!) is { } bmp)
                {
                    seenLocal.Add(photoUri);
                    seenLocal.Add(local!);
                    var groupKey = string.IsNullOrEmpty(station) ? "Survey" : $"Station {station}";
                    entries.Add(new BackupPhotoEntry(
                        bmp,
                        Path.GetFileName(local!),
                        "Survey",
                        groupKey,
                        string.IsNullOrEmpty(station) ? null : station,
                        $"shots[{i}].photos[{pi}]",
                        local));
                }

                pi++;
            }
        }
    }

    private static void AppendXRayBackdrop(
        List<BackupPhotoEntry> entries,
        CaveProjectDocument project,
        string? zipPath,
        HashSet<string> seenLocal,
        int maxEntries)
    {
        if (entries.Count >= maxEntries)
            return;
        foreach (var hint in AndroidBackupImageDiscovery.Enumerate(
                     AndroidBackupImageCategory.XRayBackdrop, project, zipPath, mapInventory: null))
        {
            if (entries.Count >= maxEntries)
                return;
            if (hint.EmbeddedJson != null)
                continue;
            if (hint.RawValue != null && seenLocal.Contains(hint.RawValue))
                continue;
            var local = AndroidBackupImageDiscovery.TryResolveLocalFile(hint, project, zipPath);
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                continue;
            if (RasterImageDecoder.TryLoadBitmap(local!) is not { } bmp)
                continue;
            if (hint.RawValue != null)
                seenLocal.Add(hint.RawValue);
            seenLocal.Add(local!);
            entries.Add(new BackupPhotoEntry(
                bmp,
                Path.GetFileName(local!),
                "X-Ray",
                "X-Ray",
                null,
                hint.SourceLabel,
                local));
        }
    }

    private static void AppendZipImages(
        List<BackupPhotoEntry> entries,
        string zipPath,
        HashSet<string> seenZipEntry,
        int maxEntries)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                if (entries.Count >= maxEntries)
                    return;
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
                if (!RasterExtensions.Contains(ext))
                    continue;
                if (seenZipEntry.Contains(entry.FullName))
                    continue;
                if (!MapAssetOpener.TryEnsureLocalFilePath(entry.FullName, zipPath, out var local, out _, null))
                    continue;
                if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                    continue;
                if (RasterImageDecoder.TryLoadBitmap(local!) is not { } bmp)
                    continue;
                seenZipEntry.Add(entry.FullName);
                var (cat, group) = ClassifyZipPath(entry.FullName);
                entries.Add(new BackupPhotoEntry(
                    bmp,
                    Path.GetFileName(entry.FullName),
                    cat,
                    group,
                    station: null,
                    sourceLabel: $"zip://{entry.FullName}",
                    localPath: local));
            }
        }
        catch
        {
            /* ignored — best-effort scan */
        }
    }

    private static (string Category, string Group) ClassifyZipPath(string path)
    {
        var p = path.Replace('\\', '/').ToLowerInvariant();
        if (p.Contains("/satellite/", StringComparison.Ordinal) || p.Contains("/xray", StringComparison.Ordinal) ||
            p.Contains("/x-ray", StringComparison.Ordinal) || p.Contains("/map_snapshot", StringComparison.Ordinal))
            return ("X-Ray", "X-Ray");
        if (p.Contains("/maps/", StringComparison.Ordinal) || p.StartsWith("maps/", StringComparison.Ordinal) ||
            p.Contains("/cartography", StringComparison.Ordinal) ||
            p.Contains("/backdrop", StringComparison.Ordinal))
            return ("Map / Backdrop", "Map / Backdrop");
        if (p.Contains("/geology", StringComparison.Ordinal) || p.Contains("/geo/", StringComparison.Ordinal) ||
            p.Contains("/rocks", StringComparison.Ordinal) || p.Contains("/mineral", StringComparison.Ordinal) ||
            p.Contains("/gemini", StringComparison.Ordinal))
            return ("Geo", "Geo");
        if (p.Contains("/biology", StringComparison.Ordinal) || p.Contains("/bio/", StringComparison.Ordinal) ||
            p.Contains("/organism", StringComparison.Ordinal) || p.Contains("/fauna", StringComparison.Ordinal) ||
            p.Contains("/flora", StringComparison.Ordinal))
            return ("Bio", "Bio");
        if (p.Contains("/photos/", StringComparison.Ordinal) || p.StartsWith("photos/", StringComparison.Ordinal))
            return ("Survey", "Survey");
        return ("Other", "Other");
    }

    private static string? TryResolveJsonSidecarDirectory(CaveProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(project.LoadedFromFile))
            return null;
        try
        {
            var full = Path.GetFullPath(project.LoadedFromFile);
            return File.Exists(full) ? Path.GetDirectoryName(full) : null;
        }
        catch
        {
            return null;
        }
    }
}
