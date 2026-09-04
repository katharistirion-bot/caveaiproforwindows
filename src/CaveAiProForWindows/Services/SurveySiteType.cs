using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Normalizes Android / Firestore site-type strings and produces map legend labels.</summary>
public static class SurveySiteType
{
    public const string JsonKey = "surveySiteType";

    public static SurveySiteTypeKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return SurveySiteTypeKind.Unknown;

        return raw.Trim().ToUpperInvariant() switch
        {
            "CAVE" => SurveySiteTypeKind.Cave,
            "MINE" => SurveySiteTypeKind.Mine,
            "POTHOLE" => SurveySiteTypeKind.Pothole,
            "SPRING" => SurveySiteTypeKind.Spring,
            _ => SurveySiteTypeKind.Unknown,
        };
    }

    /// <summary>Canonical token stored in <c>data.json</c> (uppercase).</summary>
    public static string CanonicalToken(SurveySiteTypeKind kind) =>
        kind switch
        {
            SurveySiteTypeKind.Cave => "CAVE",
            SurveySiteTypeKind.Mine => "MINE",
            SurveySiteTypeKind.Pothole => "POTHOLE",
            SurveySiteTypeKind.Spring => "SPRING",
            _ => "",
        };

    public static string NormalizeToken(string? raw)
    {
        var kind = Parse(raw);
        return kind == SurveySiteTypeKind.Unknown ? "" : CanonicalToken(kind);
    }

    /// <summary>Short English label for map title plates.</summary>
    public static string GetMapLabel(SurveySiteTypeKind kind) =>
        kind switch
        {
            SurveySiteTypeKind.Cave => "Cave",
            SurveySiteTypeKind.Mine => "Mine",
            SurveySiteTypeKind.Pothole => "Pothole",
            SurveySiteTypeKind.Spring => "Spring",
            _ => "",
        };

    /// <summary>Infer token from OSM / reference catalog free-text <c>caveType</c> labels (Android <c>SurveySiteType.inferFromCatalogLabel</c>).</summary>
    public static string InferFromCatalogLabel(string? caveType)
    {
        var t = (caveType ?? "").Trim().ToLowerInvariant();
        if (t.Contains("spring", StringComparison.Ordinal))
            return "SPRING";
        if (t.Contains("mine", StringComparison.Ordinal))
            return "MINE";
        if (t.Contains("pothole", StringComparison.Ordinal) || t.Contains("sink", StringComparison.Ordinal))
            return "POTHOLE";
        return "CAVE";
    }

    /// <summary>Canonical tokens for site-type pickers (matches Android <c>SurveySiteType.pickerTokens</c>).</summary>
    public static IReadOnlyList<string> PickerTokens { get; } =
        ["CAVE", "MINE", "POTHOLE", "SPRING"];
}
