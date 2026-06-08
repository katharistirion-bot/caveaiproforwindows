using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Visualization;

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
                tab.LoopClosureHighlights,
                tab.LrudRibbonQcHighlights,
                tab.ShowCoordinateGrid,
                showWallHatching: tab.ShowWallHatching);
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
            tab.LoopClosureHighlights,
            tab.LrudRibbonQcHighlights,
            tab.ShowCoordinateGrid,
            showWallHatching: tab.ShowWallHatching);
    }

    /// <summary>Full survey labels for SVG/PNG/office parity with on-screen Full density.</summary>
    public static PlanCanvasDrawOptions ForExport(
        SurveyCanvasKind kind,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard)
    {
        var opt = SurveyDetailDensityMapper.ToDrawOptions(
            SurveyDetailDensity.Full,
            kind,
            visualization,
            CartographicIntensity.Rich,
            overlay: true);
        return opt with { ShowCartographyOverlay = true };
    }

    /// <summary>Print-quality export preset: clean print visual mode, full labels, scale bar, north arrow.</summary>
    public static PlanCanvasDrawOptions ForExportPrint(
        SurveyCanvasKind kind,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        CaveProjectDocument? project = null,
        bool showWallHatching = true)
    {
        var opt = ForExport(kind, visualization) with
        {
            RenderPreset = CartographicRenderPreset.Print,
            ShowWallHatching = showWallHatching,
        };
        if (project != null)
        {
            opt = opt with
            {
                ExportMetadata = CaveMappingExportMetadata.FromProject(
                    project,
                    canvasKind: kind),
            };
        }

        return opt;
    }

    /// <summary>Raster export preset keyed to <see cref="MapExportQuality"/> (print adds scale bar metadata and wall hatching).</summary>
    public static PlanCanvasDrawOptions ForRasterExport(
        SurveyCanvasKind kind,
        MapExportQuality quality,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        CaveProjectDocument? project = null,
        bool showWallHatching = false)
    {
        if (quality == MapExportQuality.Print)
            return ForExportPrint(kind, visualization, project, showWallHatching);

        return ForExport(kind, visualization) with { ShowWallHatching = showWallHatching };
    }
}
