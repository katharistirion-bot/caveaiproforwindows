namespace CaveAiProForWindows.Services;

/// <summary>Mid-leg survey label in plan/section survey coordinates (metres).</summary>
public sealed record SurveyLegMapLabel(
    float MidX,
    float MidY,
    float PerpOffsetX,
    float PerpOffsetY,
    string PrimaryLine,
    string? SecondaryLine,
    string? LrudLine);

/// <summary>User-drawn depth span brace between two tap points (Android <c>DepthSpanAnnotation</c>).</summary>
public sealed record SurveyDepthSpanMapLabel(
    float X1,
    float Y1,
    float X2,
    float Y2,
    float DepthMeters,
    string Label);

/// <summary>Bracket pin with description / temperature (Android <c>CaveBracket</c>).</summary>
public sealed record SurveyBracketMapLabel(float X, float Y, string Text);

/// <summary>Compact environment / depth lines beside a station.</summary>
public sealed record SurveyStationEnvMapLabel(float X, float Y, string StationName, IReadOnlyList<string> Lines);

/// <summary>All survey-detail overlays for one map view mode.</summary>
public sealed class SurveyMapAnnotations
{
    public IReadOnlyList<SurveyLegMapLabel> LegLabels { get; init; } = Array.Empty<SurveyLegMapLabel>();
    public IReadOnlyList<SurveyDepthSpanMapLabel> DepthSpans { get; init; } = Array.Empty<SurveyDepthSpanMapLabel>();
    public IReadOnlyList<SurveyBracketMapLabel> Brackets { get; init; } = Array.Empty<SurveyBracketMapLabel>();
    public IReadOnlyList<SurveyStationEnvMapLabel> StationEnvironment { get; init; } = Array.Empty<SurveyStationEnvMapLabel>();
}
