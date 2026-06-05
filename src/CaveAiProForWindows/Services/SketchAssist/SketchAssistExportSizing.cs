namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Export pixel dimensions for sketch-assist masks (aligned with <see cref="PlanMapRasterExporter"/>).</summary>
public static class SketchAssistExportSizing
{
    public const double ExportDpiScale = PlanMapRasterExporter.ExportDpiScale;

    public const int MaxExportEdgePixels = PlanMapRasterExporter.MaxExportEdgePixels;

    public const int MinExportEdgePixels = PlanMapRasterExporter.MinExportEdgePixels;

    public const int BaseLongEdgePixels = PlanMapRasterExporter.BaseLongEdgePixels;

    public static (int PixelWidth, int PixelHeight) ComputeExportPixelSize(PlanScene scene) =>
        ComputeExportPixelSize(scene.SpanX, scene.SpanY);

    public static (int PixelWidth, int PixelHeight) ComputeExportPixelSize(float spanX, float spanY)
    {
        var aspect = spanX / Math.Max(spanY, 1e-6f);
        double bw;
        double bh;
        if (aspect >= 1)
        {
            bw = BaseLongEdgePixels;
            bh = BaseLongEdgePixels / aspect;
        }
        else
        {
            bh = BaseLongEdgePixels;
            bw = BaseLongEdgePixels * aspect;
        }

        var pxW = (int)Math.Round(bw * ExportDpiScale);
        var pxH = (int)Math.Round(bh * ExportDpiScale);
        var maxDim = Math.Max(pxW, pxH);
        if (maxDim > MaxExportEdgePixels)
        {
            var f = MaxExportEdgePixels / (double)maxDim;
            pxW = Math.Max(MinExportEdgePixels, (int)(pxW * f));
            pxH = Math.Max(MinExportEdgePixels, (int)(pxH * f));
        }

        pxW = Math.Max(MinExportEdgePixels, pxW);
        pxH = Math.Max(MinExportEdgePixels, pxH);
        return (pxW, pxH);
    }
}
