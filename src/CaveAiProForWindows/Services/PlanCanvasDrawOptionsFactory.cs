using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds <see cref="PlanCanvasDrawOptions"/> from persisted map-tab state and export presets.</summary>
public static class PlanCanvasDrawOptionsFactory
{
    public static PlanCanvasDrawOptions FromMapTab(
        MapTabPersistedState tab,
        SurveyCanvasKind kind,
        SurveyVisualizationMode visualization,
        CartographicIntensity intensity,
        bool overlay = true,
        SurveyMapPickHighlight? pick = null)
    {
        if (kind == SurveyCanvasKind.Section)
        {
            return PlanCanvasDrawOptions.ForSection(
                tab.StationNames,
                overlay,
                visualization,
                intensity,
                pick,
                tab.LegSurveyDetails,
                tab.StationEnvironment,
                tab.DepthSpanAnnotations,
                tab.BracketMarkers,
                tab.LoopClosureHighlights);
        }

        return PlanCanvasDrawOptions.ForPlan(
            tab.StationNames,
            overlay,
            visualization,
            tab.StationZ,
            intensity,
            pick,
            tab.LegSurveyDetails,
            tab.StationEnvironment,
            tab.DepthSpanAnnotations,
            tab.BracketMarkers,
            tab.LoopClosureHighlights);
    }

    /// <summary>Full survey labels for SVG/PNG/office parity with on-screen Full density.</summary>
    public static PlanCanvasDrawOptions ForExport(
        SurveyCanvasKind kind,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard) =>
        SurveyDetailDensityMapper.ToDrawOptions(
            SurveyDetailDensity.Full,
            kind,
            visualization,
            CartographicIntensity.Rich,
            overlay: true);
}
