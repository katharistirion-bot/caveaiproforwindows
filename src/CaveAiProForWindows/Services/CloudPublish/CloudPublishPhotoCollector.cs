using System.IO;
using System.IO.Compression;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Collects gallery photo bytes from an open backup ZIP for cloud publish (Android parity).</summary>
public static class CloudPublishPhotoCollector
{
    public const int MaxGalleryPhotos = 12;

    public sealed record GalleryPhoto(string FileName, byte[] Bytes, string ContentType);

    public static IReadOnlyList<GalleryPhoto> CollectFromZip(string? zipPath, CaveProjectDocument project, int maxPhotos = MaxGalleryPhotos)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath) || maxPhotos <= 0)
            return Array.Empty<GalleryPhoto>();

        var list = new List<GalleryPhoto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
            {
                if (list.Count >= maxPhotos)
                    break;
                if (entry.Length <= 0 || string.IsNullOrEmpty(entry.Name))
                    continue;
                var path = entry.FullName.Replace('\\', '/');
                if (!path.StartsWith("photos/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!IsRaster(path))
                    continue;
                if (!seen.Add(path))
                    continue;

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                var bytes = ms.ToArray();
                if (bytes.Length == 0)
                    continue;

                list.Add(new GalleryPhoto(
                    Path.GetFileName(path),
                    bytes,
                    ContentTypeForPath(path)));
            }
        }
        catch
        {
            return list;
        }

        if (list.Count >= maxPhotos)
            return list;

        foreach (var entry in BackupPhotoIndexer.Build(project, zipPath, maxPhotos * 2))
        {
            if (list.Count >= maxPhotos)
                break;
            if (string.IsNullOrWhiteSpace(entry.LocalPath) || !File.Exists(entry.LocalPath))
                continue;
            if (!IsRaster(entry.LocalPath))
                continue;
            if (!seen.Add(entry.LocalPath))
                continue;
            try
            {
                var bytes = File.ReadAllBytes(entry.LocalPath);
                if (bytes.Length == 0)
                    continue;
                list.Add(new GalleryPhoto(
                    Path.GetFileName(entry.LocalPath),
                    bytes,
                    ContentTypeForPath(entry.LocalPath)));
            }
            catch
            {
                // skip unreadable
            }
        }

        return list;
    }

    private static bool IsRaster(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string ContentTypeForPath(string path) =>
        Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
}
