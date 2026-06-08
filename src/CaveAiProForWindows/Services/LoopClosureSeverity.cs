using System.Globalization;

namespace CaveAiProForWindows.Services;

/// <summary>Loop misclosure severity for plan/section overlay colour coding.</summary>
public enum LoopClosureSeverity
{
    Good,
    Moderate,
    Large,
}

/// <summary>
/// Classifies loop misclosure (metres) using the same breakpoints as
/// <see cref="SurveyAnalysis.SurveyAnomalyScanner"/> and the loop closure assistant.
/// </summary>
public static class LoopClosureSeverityClassifier
{
    public const double GoodThresholdMetres = 0.05;
    public const double LargeThresholdMetres = 1.0;

    public static LoopClosureSeverity Classify(double misclosureMeters)
    {
        if (misclosureMeters < GoodThresholdMetres)
            return LoopClosureSeverity.Good;
        if (misclosureMeters >= LargeThresholdMetres)
            return LoopClosureSeverity.Large;
        return LoopClosureSeverity.Moderate;
    }

    public static string FormatMisclosureLabel(double misclosureMeters)
    {
        var inv = CultureInfo.InvariantCulture;
        if (misclosureMeters < 0.01)
            return $"{(misclosureMeters * 1000).ToString("0", inv)} mm";
        if (misclosureMeters < 1.0)
            return $"{misclosureMeters.ToString("0.##", inv)} m";
        return $"{misclosureMeters.ToString("0.###", inv)} m";
    }

    public static string SeverityCaption(LoopClosureSeverity severity) => severity switch
    {
        LoopClosureSeverity.Good => "good",
        LoopClosureSeverity.Moderate => "moderate",
        LoopClosureSeverity.Large => "large",
        _ => "unknown",
    };
}
