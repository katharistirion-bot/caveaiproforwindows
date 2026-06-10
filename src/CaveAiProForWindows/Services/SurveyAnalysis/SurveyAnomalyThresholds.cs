namespace CaveAiProForWindows.Services.SurveyAnalysis;

/// <summary>
/// Cross-platform anomaly / QC thresholds shared with CaveAI Pro (Android)
/// <see cref="com.caveaipro.app.SurveyAnomalyThresholds"/>.
/// Keep values in sync — see android-reference/SurveyAnomalyThresholdsContract.md.
/// </summary>
public static class SurveyAnomalyThresholds
{
    /// <summary>MAD multiplier for batch leg/LRUD outlier scan (<see cref="SurveyAnomalyScanner"/>).</summary>
    public const double MadMultiplier = 3.5;

    /// <summary>Relative factor above <see cref="MadMultiplier"/> for CRITICAL severity.</summary>
    public const double MadCriticalRelativeFactor = 1.4;

    /// <summary>Minimum traverse legs in a series before MAD scan runs.</summary>
    public const int MinSeriesSampleCount = 4;

    /// <summary>Loop misclosure below this (m) is not reported as an anomaly.</summary>
    public const double LoopMisclosureSkipMetres = 0.05;

    /// <summary>Loop misclosure at or above this (m) is WARNING (below critical).</summary>
    public const double LoopMisclosureWarningMetres = 0.35;

    /// <summary>Loop misclosure at or above this (m) is CRITICAL.</summary>
    public const double LoopMisclosureCriticalMetres = 1.0;

    /// <summary>Android pre-commit distance guard: flag when distance &gt; factor × recent mean.</summary>
    public const double AndroidDistanceOutlierMeanFactor = 3.0;

    /// <summary>Android pre-commit clino jump threshold (degrees).</summary>
    public const double AndroidClinoJumpDegrees = 40.0;
}
