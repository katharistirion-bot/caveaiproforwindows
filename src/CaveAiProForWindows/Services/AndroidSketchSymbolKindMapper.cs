using System.Collections.Generic;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Maps Android / Gson <c>symbolId</c>, labels and icon keys to built-in WPF sketch stamp geometry — keeps the Windows
/// companion visually aligned with the Android sketch editor palette.
/// </summary>
public static class AndroidSketchSymbolKindMapper
{
    /// <summary>Exact <c>symbolId</c> / glyph tokens exported by CaveAI Pro Android (snake_case and legacy camelCase).</summary>
    private static readonly Dictionary<string, SketchEditorSymbolKind> ExactIdMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["water"] = SketchEditorSymbolKind.WaterPool,
            ["water_pool"] = SketchEditorSymbolKind.WaterPool,
            ["pool"] = SketchEditorSymbolKind.WaterPool,
            ["rock"] = SketchEditorSymbolKind.RockBlock,
            ["rock_block"] = SketchEditorSymbolKind.RockBlock,
            ["boulder"] = SketchEditorSymbolKind.RockBlock,
            ["breakdown"] = SketchEditorSymbolKind.RockBlock,
            ["debris"] = SketchEditorSymbolKind.RockBlock,
            ["stal"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["stalactite"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["speleothem"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["column"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["flowstone"] = SketchEditorSymbolKind.FlowstoneCurtain,
            ["flow_stone"] = SketchEditorSymbolKind.FlowstoneCurtain,
            ["curtain"] = SketchEditorSymbolKind.FlowstoneCurtain,
            ["drape"] = SketchEditorSymbolKind.FlowstoneCurtain,
            ["sand"] = SketchEditorSymbolKind.SandMudFloor,
            ["mud"] = SketchEditorSymbolKind.SandMudFloor,
            ["sediment"] = SketchEditorSymbolKind.SandMudFloor,
            ["floor_sediment"] = SketchEditorSymbolKind.SandMudFloor,
            ["pit"] = SketchEditorSymbolKind.PitOrShaft,
            ["shaft"] = SketchEditorSymbolKind.PitOrShaft,
            ["pitch"] = SketchEditorSymbolKind.PitOrShaft,
            ["rope"] = SketchEditorSymbolKind.FixedAid,
            ["ladder"] = SketchEditorSymbolKind.FixedAid,
            ["fixed_aid"] = SketchEditorSymbolKind.FixedAid,
            ["fixedaid"] = SketchEditorSymbolKind.FixedAid,
            ["helictite"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["rimstone"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["gour"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["soda_straw"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["sodastraw"] = SketchEditorSymbolKind.StalactiteSpeleothem,
            ["pool_water"] = SketchEditorSymbolKind.WaterPool,
        };

    /// <inheritdoc cref="Resolve(string?, string?, string?)"/>
    public static SketchEditorSymbolKind Resolve(string? label, string? iconKey) =>
        Resolve(symbolId: null, label, iconKey);

    /// <summary>Prefers a stable <paramref name="symbolId"/> from JSON, then fuzzy-matches <paramref name="label"/> / <paramref name="iconKey"/>.</summary>
    public static SketchEditorSymbolKind Resolve(string? symbolId, string? label, string? iconKey)
    {
        if (TryExact(symbolId, out var k))
            return k;
        if (TryExact(label, out k))
            return k;
        if (TryExact(iconKey, out k))
            return k;
        if (TryHeuristic(label, out k))
            return k;
        if (TryHeuristic(iconKey, out k))
            return k;
        return SketchEditorSymbolKind.StalactiteSpeleothem;
    }

    private static bool TryExact(string? raw, out SketchEditorSymbolKind kind)
    {
        kind = SketchEditorSymbolKind.StalactiteSpeleothem;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (ExactIdMap.TryGetValue(s, out kind))
            return true;
        var normalized = s.Replace('_', ' ').Replace('-', ' ');
        return ExactIdMap.TryGetValue(normalized.Replace(" ", "_"), out kind);
    }

    private static bool TryHeuristic(string? raw, out SketchEditorSymbolKind kind)
    {
        kind = SketchEditorSymbolKind.StalactiteSpeleothem;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim().ToLowerInvariant();
        s = s.Replace('_', ' ').Replace('-', ' ');

        if (s.Contains("water", StringComparison.Ordinal) || s.Contains("pool", StringComparison.Ordinal) ||
            s.Contains("river", StringComparison.Ordinal) || s.Contains("lake", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.WaterPool;
            return true;
        }

        if (s.Contains("sand", StringComparison.Ordinal) || s.Contains("mud", StringComparison.Ordinal) ||
            s.Contains("sediment", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.SandMudFloor;
            return true;
        }

        if (s.Contains("flow", StringComparison.Ordinal) || s.Contains("curtain", StringComparison.Ordinal) ||
            s.Contains("drape", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.FlowstoneCurtain;
            return true;
        }

        if (s.Contains("pit", StringComparison.Ordinal) || s.Contains("shaft", StringComparison.Ordinal) ||
            s.Contains("pitch", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.PitOrShaft;
            return true;
        }

        if (s.Contains("rope", StringComparison.Ordinal) || s.Contains("ladder", StringComparison.Ordinal) ||
            s.Contains("fixed", StringComparison.Ordinal) || s.Contains("aid", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.FixedAid;
            return true;
        }

        if (s.Contains("rock", StringComparison.Ordinal) || s.Contains("block", StringComparison.Ordinal) ||
            s.Contains("boulder", StringComparison.Ordinal) || s.Contains("breakdown", StringComparison.Ordinal) ||
            s.Contains("debris", StringComparison.Ordinal) || s.Contains("choke", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.RockBlock;
            return true;
        }

        if (s.Contains("stal", StringComparison.Ordinal) || s.Contains("speleo", StringComparison.Ordinal) ||
            s.Contains("column", StringComparison.Ordinal) || s.Contains("formation", StringComparison.Ordinal) ||
            s.Contains("soda", StringComparison.Ordinal) || s.Contains("helictite", StringComparison.Ordinal))
        {
            kind = SketchEditorSymbolKind.StalactiteSpeleothem;
            return true;
        }

        return false;
    }
}
