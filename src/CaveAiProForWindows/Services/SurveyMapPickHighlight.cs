namespace CaveAiProForWindows.Services;

/// <summary>Optional map overlay for the current survey pick (station or traverse leg).</summary>
public sealed record SurveyMapPickHighlight(bool IsLeg, string StationOrFrom, string? LegToStation);
