using System.Collections.Generic;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Section map vectors use the same <see cref="PlanScene"/> pipeline as plan; rendering is implemented once in
/// <see cref="PlanCanvasRenderer"/> with <see cref="PlanCanvasDrawOptions.ForSection"/>.
/// </summary>
public static class SectionCanvasRenderer
{
    public static void Draw(
        PlanScene scene,
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        IReadOnlyList<PlanRasterUnderlay>? rasterUnderlays = null,
        CaveProjectDocument? stationImageResolveProject = null,
        string? stationImageResolveZipPath = null,
        PlanCanvasDrawOptions? drawOptions = null) =>
        PlanCanvasRenderer.Draw(
            scene,
            drawingCanvas,
            highContrast,
            canvasWidth,
            canvasHeight,
            rasterUnderlays,
            stationImageResolveProject,
            stationImageResolveZipPath,
            drawOptions);

    public static void DrawRasterUnderlaysOnly(
        Canvas drawingCanvas,
        bool highContrast,
        double canvasWidth,
        double canvasHeight,
        IReadOnlyList<PlanRasterUnderlay> underlays,
        string viewNameForLegend) =>
        PlanCanvasRenderer.DrawRasterUnderlaysOnly(
            drawingCanvas,
            highContrast,
            canvasWidth,
            canvasHeight,
            underlays,
            viewNameForLegend);
}
