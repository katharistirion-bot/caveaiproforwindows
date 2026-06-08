using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Export pixel dimensions for sketch-assist masks (aligned with <see cref="PlanMapRasterExporter"/>).</summary>
public static class SketchAssistExportSizing
{
    public const double ExportDpiScale = PlanMapRasterExporter.ExportDpiScale;

    public const int MaxExportEdgePixels = PlanMapRasterExporter.MaxExportEdgePixels;

    public const int MinExportEdgePixels = PlanMapRasterExporter.MinExportEdgePixels;

    public const int BaseLongEdgePixels = PlanMapRasterExporter.BaseLongEdgePixels;

    public static (int PixelWidth, int PixelHeight) ComputeExportPixelSize(
        PlanScene scene,
        MapExportQuality quality = MapExportQuality.Standard) =>
        ComputeExportPixelSize(scene.SpanX, scene.SpanY, quality);

    public static (int PixelWidth, int PixelHeight) ComputeExportPixelSize(
        float spanX,
        float spanY,
        MapExportQuality quality = MapExportQuality.Standard)
    {
        var (pxW, pxH) = PlanMapRasterExporter.ComputeExportPixelSize(spanX, spanY, quality);
        return (pxW, pxH);
    }
}
