namespace CaveAiProForWindows.Services;

/// <summary>Built-in stamps for cave sketch overlays (design layer).</summary>
/// <remarks>
/// IDs align with Android sketch-editor palette strings (<c>symbol</c> / <c>symbolId</c>). Alias mapping lives in
/// <see cref="AndroidSketchSymbolKindMapper"/>.
/// </remarks>
public enum SketchEditorSymbolKind
{
    RockBlock,
    WaterPool,
    StalactiteSpeleothem,
    /// <summary>Flowstone / curtain / drapery.</summary>
    FlowstoneCurtain,
    /// <summary>Sand floor, mud, sediment blanket.</summary>
    SandMudFloor,
    /// <summary>Shaft, pitch, vertical pit marker.</summary>
    PitOrShaft,
    /// <summary>Rope, ladder, fixed aids.</summary>
    FixedAid,
}
