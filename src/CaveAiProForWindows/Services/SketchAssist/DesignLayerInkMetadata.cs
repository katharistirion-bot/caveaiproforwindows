using System.Windows.Media;

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
            BrushProfile = SketchStrokeStyleDefaults.DefaultBrushProfile,
            LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            LayerName = "User ink",
        };

    public static DesignLayerInkMetadata ForProceduralWall() =>
        new()
        {
            Source = "procedural",
            StrokeWidthPx = SketchStrokeStyleDefaults.ProceduralStrokeWidthPx,
            StrokeColorArgb = SketchStrokeStyleDefaults.ProceduralStrokeColorArgb,
            BrushProfile = SketchStrokeStyleDefaults.ProceduralBrushProfile,
            LayerIndex = SketchStrokeStyleDefaults.ProceduralLayerIndex,
            LayerZOrder = SketchStrokeStyleDefaults.ProceduralLayerZOrder,
            LayerName = "Procedural walls",
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
