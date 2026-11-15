namespace CaveAiProForWindows.Models;

/// <summary>
/// Space-separated tokens on <see cref="ShotRecord.LegFlags"/>.
/// SURFACE skips cave length, DUPLICATE skips cave length, EXCLUDE leaves the network,
/// NOPLOT stays in the network but is not drawn, DONTADJUST is held during network closure.
/// </summary>
public static class SurveyLegFlags
{
    public const string Surface = "SURFACE";
    public const string Duplicate = "DUPLICATE";
    public const string Exclude = "EXCLUDE";
    public const string NoPlot = "NOPLOT";
    public const string DontAdjust = "DONTADJUST";

    public static bool Has(string? flags, string token)
    {
        if (string.IsNullOrWhiteSpace(flags) || string.IsNullOrWhiteSpace(token))
            return false;
        foreach (var part in flags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsExcluded(string? flags) => Has(flags, Exclude);

    public static bool IsNoPlot(string? flags) => Has(flags, NoPlot);

    public static bool SkipsCaveLength(string? flags) =>
        IsExcluded(flags) || Has(flags, Surface) || Has(flags, Duplicate);

    public static bool IsHeld(string? flags) => Has(flags, DontAdjust);
}
