using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Builds an immutable <see cref="SketchAssistSession"/> from the active project and Sketch Editor UI state.</summary>
public static class SketchAssistInputBuilder
{
    /// <summary>
    /// Captures survey geometry and user ink. Returns null when the project has no drawable plan scene or the canvas
    /// is too small to compute a layout.
    /// </summary>
    public static SketchAssistSession? TryBuild(
        CaveProjectDocument project,
        Canvas? designLayer,
        double canvasWidth,
        double canvasHeight,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        PlanScene? scene = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (canvasWidth < 16 || canvasHeight < 16)
            return null;

        scene ??= PlanSceneBuilder.TryBuild(
            project,
            SurveyStationGeometry.AndroidViewModePlan,
            visualization);
        if (scene == null)
            return null;

        if (!PlanCanvasRenderer.TryComputeSurveyLayout(scene, canvasWidth, canvasHeight, out var screenLayout))
            return null;

        var (strokes, stamps) = DesignLayerSurveyConverter.ExtractUserGeometry(designLayer, screenLayout);

        var document = new SketchAssistDocument
        {
            ProjectName = project.Name ?? "",
            ProjectDate = project.Date,
            SurveyMinX = scene.MinX,
            SurveyMaxX = scene.MaxX,
            SurveyMinY = scene.MinY,
            SurveyMaxY = scene.MaxY,
            UserStrokes = strokes,
            UserSymbolStamps = stamps,
        };

        return new SketchAssistSession
        {
            Project = project,
            Scene = scene,
            Document = document,
            ScreenLayout = screenLayout,
            SourceCanvasWidth = canvasWidth,
            SourceCanvasHeight = canvasHeight,
        };
    }
}
