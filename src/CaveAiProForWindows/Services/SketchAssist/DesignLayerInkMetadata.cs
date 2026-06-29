using System.Windows.Media;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Provenance and Schema v2 stroke metadata stored on design-layer <see cref="System.Windows.Shapes.Polyline"/> tags.</summary>
public sealed class DesignLayerInkMetadata
{
    public string Source { get; init; } = "designLayer";

    public double? StrokeWidthPx { get; init; }

    public uint? StrokeColorArgb { get; init; }

    public string? BrushProfile { get; init; }

    public int? LayerIndex { get; init; }

    public int? LayerZOrder { get; init; }

    public string? LayerName { get; init; }

    public static DesignLayerInkMetadata ForUserStroke(double strokeThicknessDip) =>
        new()
        {
            Source = "designLayer",
            StrokeWidthPx = strokeThicknessDip,
            StrokeColorArgb = SketchStrokeStyleDefaults.DefaultStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.Ink,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "User ink",
        };

    public static DesignLayerInkMetadata ForWallStroke(double strokeThicknessDip) =>
        new()
        {
            Source = "designLayer",
            StrokeWidthPx = strokeThicknessDip,
            StrokeColorArgb = SketchStrokeStyleDefaults.DefaultStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.Wall,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "Passage wall",
        };

    public static DesignLayerInkMetadata ForEstimatedWall(double strokeThicknessDip) =>
        new()
        {
            Source = "designLayer",
            StrokeWidthPx = strokeThicknessDip,
            StrokeColorArgb = SketchStrokeStyleDefaults.EstimatedWallStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.WallEstimated,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "Estimated wall",
        };

    public static DesignLayerInkMetadata ForFillBoundary(double strokeThicknessDip) =>
        new()
        {
            Source = "designLayer",
            StrokeWidthPx = strokeThicknessDip,
            StrokeColorArgb = SketchStrokeStyleDefaults.FillBoundaryStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.FillBoundary,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "Sand/clay boundary",
        };

    public static DesignLayerInkMetadata ForWaterStroke(double strokeThicknessDip) =>
        new()
        {
            Source = "designLayer",
            StrokeWidthPx = strokeThicknessDip,
            StrokeColorArgb = SketchStrokeStyleDefaults.WaterStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.Water,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "Water feature",
        };

    public static DesignLayerInkMetadata ForProceduralWall() =>
        new()
        {
            Source = "procedural",
            StrokeWidthPx = SketchStrokeStyleDefaults.ProceduralStrokeWidthPx,
            StrokeColorArgb = SketchStrokeStyleDefaults.ProceduralStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.Wall,
            LayerIndex = SketchStrokeStyleDefaults.ProceduralLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.ProceduralLayerZOrder,
            LayerName = "LRUD walls",
        };

    public static DesignLayerInkMetadata ForProceduralSplay() =>
        new()
        {
            Source = "procedural",
            StrokeWidthPx = SketchStrokeStyleDefaults.ProceduralStrokeWidthPx,
            StrokeColorArgb = SketchStrokeStyleDefaults.EstimatedWallStrokeColorArgb,
            BrushProfile = SketchWallInkProfiles.WallEstimated,
            LayerIndex = SketchStrokeStyleDefaults.ProceduralLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.ProceduralLayerZOrder - 1,
            LayerName = "Splay outline",
        };

    public static DesignLayerInkMetadata ForProceduralSymbol() =>
        new()
        {
            Source = "procedural",
            LayerIndex = SketchStrokeStyleDefaults.ProceduralLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.ProceduralLayerZOrder,
            LayerName = "Procedural symbols",
        };

    public static uint? ColorToArgb(Brush? brush)
    {
        if (brush is not SolidColorBrush scb)
            return null;
        var c = scb.Color;
        return ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }

    public static SolidColorBrush? ArgbToBrush(uint? argb)
    {
        if (argb == null)
            return null;
        var v = argb.Value;
        return new SolidColorBrush(Color.FromArgb(
            (byte)(v >> 24),
            (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF),
            (byte)(v & 0xFF)));
    }
}
