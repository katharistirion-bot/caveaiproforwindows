using System.IO;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Resolves <see cref="GeoBioRecord.ImageReferences"/> into in-memory <see cref="BitmapSource"/> instances.
/// Routes through <see cref="MapAssetOpener"/> for ZIP/sidecar-relative paths and
/// <see cref="OfflineEmbeddedImageDecoder"/> for inline <c>data:image</c> / base64 entries.
/// </summary>
public static class ScientificImageLoader
{
    public sealed record ResolvedImage(BitmapSource Bitmap, string Caption, string Reference);

    public static IReadOnlyList<ResolvedImage> Load(
        GeoBioRecord record,
        CaveProjectDocument project,
        string? zipPath)
    {
        var list = new List<ResolvedImage>();
        if (record.ImageReferences.Count == 0)
            return list;
        var jsonDir = TryResolveJsonSidecarDirectory(project);

        foreach (var raw in record.ImageReferences)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            // Inline base64 / data URI — decode in-memory.
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (OfflineEmbeddedImageDecoder.TryDecodeDataUriOrBase64(raw) is { } embedded)
                    list.Add(new ResolvedImage(embedded, MakeCaption(record, raw), raw));
                continue;
            }

            if (!MapAssetOpener.TryEnsureLocalFilePath(raw, zipPath, out var local, out _, jsonDir))
                continue;
            if (string.IsNullOrWhiteSpace(local) || !File.Exists(local))
                continue;
            if (RasterImageDecoder.TryLoadBitmap(local!) is { } bmp)
                list.Add(new ResolvedImage(bmp, MakeCaption(record, raw), raw));
        }

        return list;
    }

    private static string MakeCaption(GeoBioRecord record, string reference)
    {
        var leaf = reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? "(embedded)"
            : Path.GetFileName(reference.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = reference;
        return string.IsNullOrEmpty(record.Title) ? leaf : $"{record.Title} · {leaf}";
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
