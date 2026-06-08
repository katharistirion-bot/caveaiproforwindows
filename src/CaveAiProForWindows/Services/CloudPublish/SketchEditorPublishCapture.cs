using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>High-resolution cartography PNG for cloud publish (survey plan + sketch design overlay).</summary>
public static class SketchEditorPublishCapture
{
    public static byte[]? TryCaptureAiMapPng(
        CaveProjectDocument project,
        SurveyVisualizationMode visualizationMode,
        PlanCanvasDrawOptions drawOptions,
        IReadOnlyList<PlanRasterUnderlay> underlays,
        string? zipPath,
        Canvas? designLayer,
        double editorCanvasWidth,
        double editorCanvasHeight)
    {
        ArgumentNullException.ThrowIfNull(project);

        PlanDesignLayerExportContext? overlay = designLayer != null && editorCanvasWidth >= 1 && editorCanvasHeight >= 1
            ? new PlanDesignLayerExportContext(designLayer, editorCanvasWidth, editorCanvasHeight)
            : null;

        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            visualizationMode,
            highContrast: false,
            drawOptions,
            underlays,
            zipPath,
            quality: MapExportQuality.Print,
            designOverlay: overlay);
    }
}
