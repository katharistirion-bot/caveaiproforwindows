using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services;

/// <summary>Maps CaveAI symbol keys to Therion <c>point</c> types.</summary>
public static class TherionSymbolMapper
{
    public static string ToTherionPointType(string? iconKey, string? symbolId, string? label)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
            return SketchKindToTherion(SketchEditorSymbolKindExporter.FromSymbolId(symbolId));

        if (CaveMappingSymbolCatalog.TryParseIconKey(iconKey, out var kind))
            return KindToTherion(kind);

        if (CaveMappingSymbolCatalog.TryParseIconKey(symbolId, out kind))
            return KindToTherion(kind);

        var hay = $"{iconKey} {symbolId} {label}".ToLowerInvariant();
        if (hay.Contains("water", StringComparison.Ordinal)) return "water-pond";
        if (hay.Contains("stalact", StringComparison.Ordinal)) return "stalactite";
        if (hay.Contains("stalagm", StringComparison.Ordinal)) return "stalagmite";
        if (hay.Contains("flow", StringComparison.Ordinal) || hay.Contains("curtain", StringComparison.Ordinal))
            return "stalactite";
        if (hay.Contains("sand", StringComparison.Ordinal) || hay.Contains("sediment", StringComparison.Ordinal))
            return "sand";
        if (hay.Contains("pit", StringComparison.Ordinal) || hay.Contains("pitch", StringComparison.Ordinal))
            return "pit";
        if (hay.Contains("aven", StringComparison.Ordinal)) return "air-break";
        if (hay.Contains("stream", StringComparison.Ordinal)) return "water-flow";
        if (hay.Contains("rope", StringComparison.Ordinal) || hay.Contains("ladder", StringComparison.Ordinal))
            return "rope";
        if (hay.Contains("choke", StringComparison.Ordinal)) return "air-break";
        if (hay.Contains("break", StringComparison.Ordinal) || hay.Contains("block", StringComparison.Ordinal))
            return "blocks";
        if (hay.Contains("column", StringComparison.Ordinal)) return "stalagmite";
        if (hay.Contains("helict", StringComparison.Ordinal)) return "helictite";
        if (hay.Contains("guano", StringComparison.Ordinal) || hay.Contains("bat", StringComparison.Ordinal))
            return "bat";
        if (hay.Contains("bone", StringComparison.Ordinal) || hay.Contains("archae", StringComparison.Ordinal))
            return "archeology";
        if (hay.Contains("entrance", StringComparison.Ordinal)) return "entrance";

        return "air-break";
    }

    private static string SketchKindToTherion(SketchEditorSymbolKind kind) =>
        kind switch
        {
            SketchEditorSymbolKind.WaterPool => "water-pond",
            SketchEditorSymbolKind.StalactiteSpeleothem => "stalactite",
            SketchEditorSymbolKind.FlowstoneCurtain => "stalactite",
            SketchEditorSymbolKind.SandMudFloor => "sand",
            SketchEditorSymbolKind.MudDeposit => "sand",
            SketchEditorSymbolKind.PitOrShaft => "pit",
            SketchEditorSymbolKind.AvenShaftUp => "air-break",
            SketchEditorSymbolKind.FixedAid => "rope",
            SketchEditorSymbolKind.Choke => "air-break",
            SketchEditorSymbolKind.BreakdownPile => "blocks",
            SketchEditorSymbolKind.RockBlock => "blocks",
            SketchEditorSymbolKind.ColumnPillar => "stalagmite",
            SketchEditorSymbolKind.Helictite => "helictite",
            SketchEditorSymbolKind.SubterraneanStream => "water-flow",
            SketchEditorSymbolKind.ArchaeologyBones => "archeology",
            SketchEditorSymbolKind.BatGuano => "bat",
            SketchEditorSymbolKind.CrackFissure => "air-break",
            _ => "air-break",
        };

    private static string KindToTherion(CaveMappingSymbolCatalog.SymbolKind kind) =>
        kind switch
        {
            CaveMappingSymbolCatalog.SymbolKind.WaterPool => "water-pond",
            CaveMappingSymbolCatalog.SymbolKind.Stalactite => "stalactite",
            CaveMappingSymbolCatalog.SymbolKind.Stalagmite => "stalagmite",
            CaveMappingSymbolCatalog.SymbolKind.Breakdown => "blocks",
            CaveMappingSymbolCatalog.SymbolKind.RockBlock => "blocks",
            CaveMappingSymbolCatalog.SymbolKind.Entrance => "entrance",
            CaveMappingSymbolCatalog.SymbolKind.StationMarker => "station-name",
            CaveMappingSymbolCatalog.SymbolKind.JunctionMarker => "air-break",
            _ => "air-break",
        };
}
