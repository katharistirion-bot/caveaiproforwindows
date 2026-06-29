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
    /// <summary>Narrow choke / constriction.</summary>
    Choke,
    /// <summary>Breakdown / boulder pile / debris field.</summary>
    BreakdownPile,
    /// <summary>Column / pillar (stalagmite-stalactite join).</summary>
    ColumnPillar,
    /// <summary>Helictite / eccentric speleothem.</summary>
    Helictite,
    /// <summary>Aven / upward shaft (UIS aven).</summary>
    AvenShaftUp,
    /// <summary>Subterranean stream (UIS stream).</summary>
    SubterraneanStream,
    /// <summary>Mud deposit (UIS mud).</summary>
    MudDeposit,
    /// <summary>Bones / archaeological find (UIS bones).</summary>
    ArchaeologyBones,
    /// <summary>Bat guano (UIS guano).</summary>
    BatGuano,
    /// <summary>Rock crack / fissure (passage edge hint).</summary>
    CrackFissure,
}
