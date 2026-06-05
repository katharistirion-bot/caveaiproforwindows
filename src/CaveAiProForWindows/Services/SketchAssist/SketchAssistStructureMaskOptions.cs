namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Controls which layers are drawn into the grey/black structure mask PNG.</summary>
public sealed class SketchAssistStructureMaskOptions
{
    /// <summary>Draw LRUD left/right corridor polygons (preferred for generative ControlNet).</summary>
    public bool IncludeLrudCorridor { get; init; } = true;

    /// <summary>Thin traverse centerline when <see cref="IncludeLrudCorridor"/> is false.</summary>
    public bool IncludeTraverseCenterline { get; init; }

    /// <summary>Draw freehand strokes from <see cref="SketchAssistDocument.UserStrokes"/>.</summary>
    public bool IncludeUserStrokes { get; init; } = true;

    /// <summary>Draw small discs at user symbol stamp anchors.</summary>
    public bool IncludeUserSymbolStamps { get; init; } = true;

    /// <summary>Line width in export pixels for traverse and stroke geometry.</summary>
    public double LineWidthPx { get; init; } = 2.5;

    /// <summary>Radius in export pixels for symbol stamp markers.</summary>
    public double SymbolMarkerRadiusPx { get; init; } = 6;

    public static SketchAssistStructureMaskOptions Default { get; } = new();
}
