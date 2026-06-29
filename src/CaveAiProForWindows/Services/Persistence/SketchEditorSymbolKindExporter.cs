namespace CaveAiProForWindows.Services.Persistence;

/// <summary>Maps WPF sketch stamp kinds to Android <c>symbolId</c> strings for <c>mapObjects</c> JSON.</summary>
public static class SketchEditorSymbolKindExporter
{
    public static string ToSymbolId(SketchEditorSymbolKind kind) =>
        kind switch
        {
            SketchEditorSymbolKind.WaterPool => "water",
            SketchEditorSymbolKind.RockBlock => "rock",
            SketchEditorSymbolKind.FlowstoneCurtain => "flowstone",
            SketchEditorSymbolKind.SandMudFloor => "sand",
            SketchEditorSymbolKind.PitOrShaft => "pit",
            SketchEditorSymbolKind.FixedAid => "ladder",
            SketchEditorSymbolKind.Choke => "choke",
            SketchEditorSymbolKind.BreakdownPile => "breakdown",
            SketchEditorSymbolKind.ColumnPillar => "column",
            SketchEditorSymbolKind.Helictite => "helictite",
            SketchEditorSymbolKind.AvenShaftUp => "aven",
            SketchEditorSymbolKind.SubterraneanStream => "stream",
            SketchEditorSymbolKind.MudDeposit => "mud",
            SketchEditorSymbolKind.ArchaeologyBones => "bones",
            SketchEditorSymbolKind.BatGuano => "guano",
            SketchEditorSymbolKind.CrackFissure => "crack",
            _ => "stalactite",
        };

    public static SketchEditorSymbolKind FromSymbolId(string? symbolId) =>
        AndroidSketchSymbolKindMapper.Resolve(symbolId, null, null);
}
