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
}
