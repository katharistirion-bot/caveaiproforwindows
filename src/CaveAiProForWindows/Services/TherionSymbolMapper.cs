using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services;

/// <summary>Maps CaveAI symbol keys to Therion <c>point</c> types.</summary>
public static class TherionSymbolMapper
{
    public static string ToTherionPointType(string? iconKey, string? symbolId, string? label)
    {
        if (CaveMappingSymbolCatalog.TryParseIconKey(iconKey, out var kind))
            return KindToTherion(kind);

        if (CaveMappingSymbolCatalog.TryParseIconKey(symbolId, out kind))
            return KindToTherion(kind);

        var hay = $"{iconKey} {symbolId} {label}".ToLowerInvariant();
        if (hay.Contains("water", StringComparison.Ordinal)) return "water-pond";
        if (hay.Contains("stalact", StringComparison.Ordinal)) return "stalactite";
        if (hay.Contains("stalagm", StringComparison.Ordinal)) return "stalagmite";
        if (hay.Contains("break", StringComparison.Ordinal) || hay.Contains("block", StringComparison.Ordinal))
            return "blocks";
        if (hay.Contains("entrance", StringComparison.Ordinal)) return "entrance";

        return "air-break";
    }

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
