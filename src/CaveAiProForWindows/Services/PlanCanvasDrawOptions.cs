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
    SurveyCanvasKind CanvasKind = SurveyCanvasKind.Plan,
    SurveyVisualizationMode VisualizationMode = SurveyVisualizationMode.Standard)
{
    public static PlanCanvasDrawOptions ForPlan(bool stationNames, bool overlay, SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard) =>
        new(stationNames, overlay, SurveyCanvasKind.Plan, visualization);

    public static PlanCanvasDrawOptions ForSection(bool stationNames, bool overlay, SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard) =>
        new(stationNames, overlay, SurveyCanvasKind.Section, visualization);
}
