namespace CaveAiProForWindows.Services.SurveyAnalysis;

public enum SurveyAnomalyKind
{
    LegDistance,
    Azimuth,
    Clino,
    LrudLeft,
    LrudRight,
    LrudUp,
    LrudDown,
    LoopMisclosure,
}

/// <summary>One locally computed QC finding — no cloud inference.</summary>
public sealed record SurveyAnomalyFinding(
    SurveyAnomalyKind Kind,
    string StationOrLeg,
    double ObservedValue,
    double ExpectedOrMedian,
    double RobustSigma,
    double ZScore,
    string Detail,
    SurveyAnomalySeverity Severity);

public enum SurveyAnomalySeverity
{
    Info,
    Warning,
    Critical,
}
