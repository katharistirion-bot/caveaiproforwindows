namespace CaveAiProForWindows.Services;

/// <summary>Plan / section canvas options aligned with typical cave survey cartography (labels, scale bar).</summary>
public enum SurveyCanvasKind
{
    Plan,
    Section,
}

/// <summary>Overlay options for plan/section canvas rendering (station labels, scale bar — familiar from cave survey packages).</summary>
public sealed record PlanCanvasDrawOptions(
    bool ShowStationNames = false,
    bool ShowCartographyOverlay = true,
    SurveyCanvasKind CanvasKind = SurveyCanvasKind.Plan)
{
    public static PlanCanvasDrawOptions ForPlan(bool stationNames, bool overlay) =>
        new(stationNames, overlay, SurveyCanvasKind.Plan);

    public static PlanCanvasDrawOptions ForSection(bool stationNames, bool overlay) =>
        new(stationNames, overlay, SurveyCanvasKind.Section);
}
