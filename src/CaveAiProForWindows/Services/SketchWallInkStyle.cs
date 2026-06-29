using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.Services;

public static class SketchWallInkStyle
{
    public static void ApplyToPolyline(Polyline poly, DesignLayerInkMetadata meta)
    {
        poly.Stroke = DesignLayerInkMetadata.ArgbToBrush(meta.StrokeColorArgb) ?? Brushes.Black;
        poly.StrokeThickness = meta.StrokeWidthPx ?? SketchStrokeStyleDefaults.DefaultStrokeWidthPx;
        poly.StrokeLineJoin = PenLineJoin.Round;
        poly.StrokeStartLineCap = PenLineCap.Round;
        poly.StrokeEndLineCap = PenLineCap.Round;
        poly.StrokeDashArray = ResolveDash(meta.BrushProfile);
        poly.Opacity = string.Equals(meta.BrushProfile, SketchWallInkProfiles.WallEstimated, StringComparison.OrdinalIgnoreCase)
            ? 0.88
            : 1.0;
        poly.Tag = meta;
    }

    public static DoubleCollection? ResolveDash(string? brushProfile) =>
        brushProfile switch
        {
            SketchWallInkProfiles.WallEstimated => new DoubleCollection { 4, 3 },
            SketchWallInkProfiles.FillBoundary => new DoubleCollection { 1.5, 2.5 },
            _ => null,
        };

    public static DesignLayerInkMetadata CreateUserStrokeMetadata(double widthPx, string wallProfile, bool dashedInk)
    {
        if (string.Equals(wallProfile, SketchWallInkProfiles.Wall, StringComparison.OrdinalIgnoreCase))
            return DesignLayerInkMetadata.ForWallStroke(widthPx);
        if (string.Equals(wallProfile, SketchWallInkProfiles.WallEstimated, StringComparison.OrdinalIgnoreCase))
            return DesignLayerInkMetadata.ForEstimatedWall(widthPx);
        if (string.Equals(wallProfile, SketchWallInkProfiles.FillBoundary, StringComparison.OrdinalIgnoreCase))
            return DesignLayerInkMetadata.ForFillBoundary(widthPx);
        if (string.Equals(wallProfile, SketchWallInkProfiles.Water, StringComparison.OrdinalIgnoreCase))
            return DesignLayerInkMetadata.ForWaterStroke(widthPx);

        var meta = DesignLayerInkMetadata.ForUserStroke(widthPx);
        if (!dashedInk)
            return meta;

        return new DesignLayerInkMetadata
        {
            Source = meta.Source,
            StrokeWidthPx = meta.StrokeWidthPx,
            StrokeColorArgb = meta.StrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.WallEstimated,
            LayerIndex = meta.LayerIndex,
            LayerZOrder = meta.LayerZOrder,
            LayerName = "Estimated ink",
        };
    }
}
