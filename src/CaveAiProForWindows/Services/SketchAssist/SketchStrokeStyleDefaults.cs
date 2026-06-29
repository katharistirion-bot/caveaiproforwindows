using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Default stroke style values aligned with Android Schema v2 sketch metadata.</summary>
public static class SketchStrokeStyleDefaults
{
    public const double DefaultStrokeWidthPx = 1.5;

    public const uint DefaultStrokeColorArgb = 0xFF000000;

    public const string DefaultBrushProfile = SketchWallInkProfiles.Ink;

    public const double ProceduralStrokeWidthPx = 1.2;

    public const uint ProceduralStrokeColorArgb = 0xFF000000;

    public const string ProceduralBrushProfile = SketchWallInkProfiles.Wall;

    public const uint EstimatedWallStrokeColorArgb = 0xFF505050;

    public const uint FillBoundaryStrokeColorArgb = 0xFF8B6914;

    public const uint WaterStrokeColorArgb = 0xFF1E6FA8;

    public const int ProceduralLayerIndex = 0;

    public const int ProceduralLayerZOrder = 0;

    public const int UserLayerIndex = 1;

    public const int UserLayerZOrder = 10;

    public const string ProceduralSource = "procedural";

    public const string DesignLayerSource = "designLayer";
}

/// <summary>UIS / Therion-style stroke semantics exported as <c>brushProfile</c> on mapObjects.</summary>
public static class SketchWallInkProfiles
{
    public const string Ink = "ink";
    public const string Wall = "wall";
    public const string WallEstimated = "wall_estimated";
    public const string FillBoundary = "fill_boundary";
    public const string Water = "water";
    public const string Procedural = "procedural";
}
