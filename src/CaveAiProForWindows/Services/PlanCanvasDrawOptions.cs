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
    SurveyVisualizationMode VisualizationMode = SurveyVisualizationMode.Standard,
    bool ShowStationZDepth = false,
    CartographicIntensity CartographicIntensity = CartographicIntensity.Balanced,
    SurveyMapPickHighlight? PickHighlight = null,
    bool ShowLegSurveyDetails = true,
    bool ShowStationEnvironment = true,
    bool ShowDepthSpanAnnotations = true,
    bool ShowBracketMarkers = true,
    bool ShowLoopClosureHighlights = true)
{
    public int AnnotationViewMode =>
        CanvasKind == SurveyCanvasKind.Section
            ? SurveyStationGeometry.AndroidViewModeSection
            : VisualizationMode == SurveyVisualizationMode.LongProfile
                ? SurveyStationGeometry.AndroidViewModeLongProfile
                : SurveyStationGeometry.AndroidViewModePlan;

    public static PlanCanvasDrawOptions ForPlan(
        bool stationNames,
        bool overlay,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        bool showStationZDepth = false,
        CartographicIntensity cartographicIntensity = CartographicIntensity.Balanced,
        SurveyMapPickHighlight? pickHighlight = null,
        bool showLegSurveyDetails = true,
        bool showStationEnvironment = true,
        bool showDepthSpanAnnotations = true,
        bool showBracketMarkers = true,
        bool showLoopClosureHighlights = true) =>
        new(stationNames, overlay, SurveyCanvasKind.Plan, visualization, showStationZDepth, cartographicIntensity,
            pickHighlight, showLegSurveyDetails, showStationEnvironment, showDepthSpanAnnotations, showBracketMarkers,
            showLoopClosureHighlights);

    public static PlanCanvasDrawOptions ForSection(
        bool stationNames,
        bool overlay,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        CartographicIntensity cartographicIntensity = CartographicIntensity.Balanced,
        SurveyMapPickHighlight? pickHighlight = null,
        bool showLegSurveyDetails = true,
        bool showStationEnvironment = true,
        bool showDepthSpanAnnotations = true,
        bool showBracketMarkers = true,
        bool showLoopClosureHighlights = true) =>
        new(stationNames, overlay, SurveyCanvasKind.Section, visualization, ShowStationZDepth: false,
            cartographicIntensity, pickHighlight, showLegSurveyDetails, showStationEnvironment,
            showDepthSpanAnnotations, showBracketMarkers, showLoopClosureHighlights);
}
