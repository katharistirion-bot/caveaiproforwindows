namespace CaveAiProForWindows.Services;

using CaveAiProForWindows.Services.Visualization;

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
    bool ShowLoopClosureHighlights = true,
    bool ShowLrudQcHighlights = true,
    bool ShowCoordinateGrid = false,
    bool ShowSymbolLegend = false,
    bool ShowWallHatching = false,
    CartographicRenderPreset RenderPreset = CartographicRenderPreset.Field,
    CaveMappingExportMetadata? ExportMetadata = null)
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
        bool showLoopClosureHighlights = true,
        bool showLrudQcHighlights = true,
        bool showCoordinateGrid = false,
        bool showSymbolLegend = false,
        bool showWallHatching = false,
        CartographicRenderPreset renderPreset = CartographicRenderPreset.Field,
        CaveMappingExportMetadata? exportMetadata = null) =>
        new(stationNames, overlay, SurveyCanvasKind.Plan, visualization, showStationZDepth, cartographicIntensity,
            pickHighlight, showLegSurveyDetails, showStationEnvironment, showDepthSpanAnnotations, showBracketMarkers,
            showLoopClosureHighlights, showLrudQcHighlights, showCoordinateGrid, showSymbolLegend, showWallHatching,
            renderPreset, exportMetadata);

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
        bool showLoopClosureHighlights = true,
        bool showLrudQcHighlights = true,
        bool showCoordinateGrid = false,
        bool showSymbolLegend = false,
        bool showWallHatching = false,
        CartographicRenderPreset renderPreset = CartographicRenderPreset.Field,
        CaveMappingExportMetadata? exportMetadata = null) =>
        new(stationNames, overlay, SurveyCanvasKind.Section, visualization, ShowStationZDepth: false,
            cartographicIntensity, pickHighlight, showLegSurveyDetails, showStationEnvironment,
            showDepthSpanAnnotations, showBracketMarkers, showLoopClosureHighlights, showLrudQcHighlights,
            showCoordinateGrid, showSymbolLegend, showWallHatching, renderPreset, exportMetadata);
}
