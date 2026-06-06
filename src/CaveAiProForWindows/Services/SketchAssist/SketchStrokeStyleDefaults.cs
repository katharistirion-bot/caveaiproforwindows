namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Default stroke style values aligned with Android Schema v2 sketch metadata.</summary>
public static class SketchStrokeStyleDefaults
{
    public const double DefaultStrokeWidthPx = 1.5;

    public const uint DefaultStrokeColorArgb = 0xFF000000;

    public const string DefaultBrushProfile = "ink";

    public const double ProceduralStrokeWidthPx = 1.2;

    public const uint ProceduralStrokeColorArgb = 0xFF505050;

    public const string ProceduralBrushProfile = "procedural";

    public const int ProceduralLayerIndex = 0;

    public const int ProceduralLayerZOrder = 0;

    public const int UserLayerIndex = 1;

    public const int UserLayerZOrder = 10;

    public const string ProceduralSource = "procedural";

    public const string DesignLayerSource = "designLayer";
}
