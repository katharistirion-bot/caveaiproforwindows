using System.IO;
using CaveAiProForWindows.Models;
using SkiaSharp;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Samples elevation from a GeoTIFF / DEM raster using survey-metre world extent from project metadata.
/// </summary>
public static class GeoTiffElevationSampler
{
    public sealed class DemSource : IDisposable
    {
        public required SKBitmap Bitmap { get; init; }

        public required PlanRasterWorldExtentMetres Extent { get; init; }

        public required string SourcePath { get; init; }

        public void Dispose() => Bitmap.Dispose();
    }

    public static DemSource? TryOpenDem(CaveProjectDocument project, string? zipPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        var baseDir = Path.GetDirectoryName(project.LoadedFromFile ?? zipPath ?? "");
        var rows = MapAssetsCollector.Collect(new[] { project });
        foreach (var row in rows)
        {
            var path = (row.UriOrPath ?? "").Trim();
            if (path.Length == 0)
                continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".tif" or ".tiff" or ".geotiff"))
                continue;

            if (!MapAssetOpener.TryEnsureLocalFilePath(path, zipPath, out var local, out _, baseDir) ||
                string.IsNullOrWhiteSpace(local) ||
                !File.Exists(local))
                continue;

            var extent = RasterUnderlayBoundsParser.TryParse(project, row, local);
            if (extent == null)
                continue;

            try
            {
                using var stream = File.OpenRead(local);
                using var codec = SKCodec.Create(stream);
                if (codec == null)
                    continue;
                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                var bmp = new SKBitmap(info);
                if (codec.GetPixels(info, bmp.GetPixels()) != SKCodecResult.Success)
                {
                    bmp.Dispose();
                    continue;
                }

                return new DemSource { Bitmap = bmp, Extent = extent, SourcePath = local };
            }
            catch
            {
                /* try next candidate */
            }
        }

        return null;
    }

    public static double SampleElevation(DemSource source, double worldX, double worldY, double fallbackZ)
    {
        var ext = source.Extent;
        if (worldX < ext.MinX || worldX > ext.MaxX || worldY < ext.MinY || worldY > ext.MaxY)
            return fallbackZ;

        var u = (worldX - ext.MinX) / Math.Max(ext.MaxX - ext.MinX, 1e-6);
        var v = 1.0 - (worldY - ext.MinY) / Math.Max(ext.MaxY - ext.MinY, 1e-6);
        var px = u * (source.Bitmap.Width - 1);
        var py = v * (source.Bitmap.Height - 1);
        var ix = (int)Math.Clamp(Math.Round(px), 0, source.Bitmap.Width - 1);
        var iy = (int)Math.Clamp(Math.Round(py), 0, source.Bitmap.Height - 1);
        var c = source.Bitmap.GetPixel(ix, iy);
        var gray = (c.Red + c.Green + c.Blue) / (3.0 * 255.0);
        return fallbackZ + (gray - 0.5) * 20.0;
    }
}
