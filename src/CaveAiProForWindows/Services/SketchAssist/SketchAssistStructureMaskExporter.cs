using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>
/// Exports a high-resolution structure mask: LRUD corridor polygons (grey) and user sketch (white) on black —
/// suitable for ControlNet img2img pipelines.
/// </summary>
public static class SketchAssistStructureMaskExporter
{
    private static readonly Brush MaskBackground = Brushes.Black;
    private static readonly Brush MaskInk = Brushes.White;

    /// <summary>PNG bytes (PBGRA) or null when the session scene cannot be laid out at export size.</summary>
    public static byte[]? TryExportStructureMaskPng(
        SketchAssistSession session,
        SketchAssistStructureMaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        options ??= SketchAssistStructureMaskOptions.Default;

        return StructureMaskBuilder.TryBuildGrayscaleLrudMaskPng(session, options);
    }

    /// <summary>
    /// Convenience: build session from UI state and export in one call.
    /// </summary>
    public static byte[]? TryExportStructureMaskPngFromEditor(
        CaveProjectDocument project,
        Canvas? designLayer,
        double canvasWidth,
        double canvasHeight,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        SketchAssistStructureMaskOptions? options = null)
    {
        var session = SketchAssistInputBuilder.TryBuild(
            project,
            designLayer,
            canvasWidth,
            canvasHeight,
            visualization);
        return session == null ? null : TryExportStructureMaskPng(session, options);
    }

    internal static void DrawTraverseCenterlineForMask(
        Canvas canvas,
        PlanScene scene,
        PlanCanvasSurveyLayout layout,
        double lineWidthPx)
    {
        foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
        {
            var p1 = layout.WorldToCanvas(x1, y1);
            var p2 = layout.WorldToCanvas(x2, y2);
            AddLine(canvas, p1, p2, lineWidthPx);
        }
    }

    internal static void DrawUserStrokesForMask(
        Canvas canvas,
        SketchAssistDocument document,
        PlanCanvasSurveyLayout layout,
        double lineWidthPx)
    {
        foreach (var stroke in document.UserStrokes)
        {
            if (!stroke.IsDrawable)
                continue;

            var poly = new Polyline
            {
                Stroke = MaskInk,
                StrokeThickness = lineWidthPx,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
            };

            foreach (var (x, y) in stroke.Points)
                poly.Points.Add(layout.WorldToCanvas(x, y));

            canvas.Children.Add(poly);
        }
    }

    internal static void DrawUserSymbolStampsForMask(
        Canvas canvas,
        SketchAssistDocument document,
        PlanCanvasSurveyLayout layout,
        double radiusPx)
    {
        var diameter = Math.Max(2, radiusPx * 2);
        foreach (var stamp in document.UserSymbolStamps)
        {
            var center = layout.WorldToCanvas(stamp.SurveyX, stamp.SurveyY);
            var dot = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = MaskInk,
                Stroke = MaskInk,
                StrokeThickness = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(dot, center.X - radiusPx);
            Canvas.SetTop(dot, center.Y - radiusPx);
            canvas.Children.Add(dot);
        }
    }

    private static void AddLine(Canvas canvas, Point p1, Point p2, double lineWidthPx)
    {
        var line = new Line
        {
            X1 = p1.X,
            Y1 = p1.Y,
            X2 = p2.X,
            Y2 = p2.Y,
            Stroke = MaskInk,
            StrokeThickness = lineWidthPx,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(line);
    }
}

