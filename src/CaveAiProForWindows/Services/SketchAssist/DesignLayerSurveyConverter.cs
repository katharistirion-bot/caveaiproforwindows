using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>
/// Maps WPF <see cref="Canvas"/> design-layer visuals (screen DIPs local to the survey canvas) into survey metres
/// using <see cref="PlanCanvasSurveyLayout"/>.
/// </summary>
public static class DesignLayerSurveyConverter
{
    /// <summary>
    /// Extracts user freehand polylines and symbol stamps from <paramref name="designLayer"/>.
    /// Skips Android-imported symbols (<see cref="AndroidImportedSymbolPresenter.ImportedChildTag"/>).
    /// </summary>
    public static (List<SketchStrokeModel> Strokes, List<SketchSymbolStampModel> SymbolStamps) ExtractUserGeometry(
        Canvas? designLayer,
        PlanCanvasSurveyLayout layout)
    {
        var strokes = new List<SketchStrokeModel>();
        var stamps = new List<SketchSymbolStampModel>();
        if (designLayer == null)
            return (strokes, stamps);

        foreach (UIElement child in designLayer.Children)
        {
            if (child is FrameworkElement fe && Equals(fe.Tag, AndroidImportedSymbolPresenter.ImportedChildTag))
                continue;

            switch (child)
            {
                case Polyline poly when poly.Points.Count >= 2:
                    if (TryConvertPolyline(poly, layout, out var stroke))
                        strokes.Add(stroke);
                    break;
                case Viewbox vb:
                    if (TryConvertSymbolViewbox(vb, layout, out var stamp))
                        stamps.Add(stamp);
                    break;
                case Ellipse:
                    // Transient select marker from MapCanvasEditorController — ignore.
                    break;
            }
        }

        return (strokes, stamps);
    }

    /// <summary>Converts one canvas DIP point to survey metres.</summary>
    public static (float Wx, float Wy) CanvasPointToSurvey(double canvasX, double canvasY, PlanCanvasSurveyLayout layout)
    {
        var (wx, wy) = layout.CanvasToWorld(canvasX, canvasY);
        return ((float)wx, (float)wy);
    }

    /// <summary>Converts survey metres to canvas DIPs (same mapping as plan vector rendering).</summary>
    public static Point SurveyPointToCanvas(float surveyX, float surveyY, PlanCanvasSurveyLayout layout) =>
        layout.WorldToCanvas(surveyX, surveyY);

    private static bool TryConvertPolyline(
        Polyline poly,
        PlanCanvasSurveyLayout layout,
        out SketchStrokeModel stroke)
    {
        stroke = new SketchStrokeModel { Source = "designLayer" };
        foreach (var pt in poly.Points)
        {
            var (wx, wy) = CanvasPointToSurvey(pt.X, pt.Y, layout);
            if (!IsFinite(wx) || !IsFinite(wy))
                continue;
            stroke.Points.Add((wx, wy));
        }

        return stroke.IsDrawable;
    }

    private static bool TryConvertSymbolViewbox(
        Viewbox vb,
        PlanCanvasSurveyLayout layout,
        out SketchSymbolStampModel stamp)
    {
        stamp = default!;
        var left = Canvas.GetLeft(vb);
        var top = Canvas.GetTop(vb);
        if (double.IsNaN(left))
            left = 0;
        if (double.IsNaN(top))
            top = 0;
        var w = vb.Width > 0 ? vb.Width : vb.ActualWidth;
        var h = vb.Height > 0 ? vb.Height : vb.ActualHeight;
        if (w <= 0)
            w = SketchSymbolDefinitions.StampDisplaySize;
        if (h <= 0)
            h = SketchSymbolDefinitions.StampDisplaySize;
        var cx = left + w * 0.5;
        var cy = top + h * 0.5;
        var (wx, wy) = CanvasPointToSurvey(cx, cy, layout);
        if (!IsFinite(wx) || !IsFinite(wy))
            return false;

        stamp = new SketchSymbolStampModel
        {
            SurveyX = wx,
            SurveyY = wy,
            Kind = ResolveSymbolKind(vb),
        };
        return true;
    }

    private static SketchEditorSymbolKind ResolveSymbolKind(Viewbox vb)
    {
        if (vb.Child is not Path path || path.Data == null)
            return SketchEditorSymbolKind.StalactiteSpeleothem;

        foreach (SketchEditorSymbolKind kind in Enum.GetValues(typeof(SketchEditorSymbolKind)))
        {
            try
            {
                var ink = SketchSymbolDefinitions.Get(kind);
                if (ReferenceEquals(ink.Geometry, path.Data))
                    return kind;
            }
            catch
            {
                /* ignore */
            }
        }

        return SketchEditorSymbolKind.StalactiteSpeleothem;
    }

    private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
}
