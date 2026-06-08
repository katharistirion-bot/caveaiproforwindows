using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Survey-metre coordinate grid for plan/section canvases.</summary>
public static class SurveyCoordinateGridRenderer
{
    private static readonly double[] NiceSteps = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];

    /// <summary>
    /// Picks a grid spacing (metres) so lines are roughly 40–100 px apart at the current zoom.
    /// </summary>
    public static double ResolveGridSpacingMetres(double pxPerMetre, double spanMetres)
    {
        if (pxPerMetre <= 0 || double.IsNaN(pxPerMetre) || double.IsInfinity(pxPerMetre))
            return 10;

        const double targetPx = 60;
        var raw = targetPx / pxPerMetre;
        if (raw <= 0)
            raw = spanMetres > 0 ? spanMetres / 8.0 : 10;

        foreach (var step in NiceSteps)
        {
            if (step >= raw * 0.85)
                return step;
        }

        return NiceSteps[^1];
    }

    public static void Draw(
        Canvas canvas,
        PlanScene scene,
        PlanCanvasSurveyLayout layout,
        bool darkCanvas,
        Action<Canvas, UIElement, int>? addChildZ = null,
        int zIndex = 0)
    {
        var spacing = ResolveGridSpacingMetres(layout.PxPerMetre, Math.Max(scene.SpanX, scene.SpanY));
        var majorBrush = new SolidColorBrush(
            darkCanvas ? Color.FromArgb(40, 140, 155, 175) : Color.FromArgb(44, 110, 100, 88));
        var minorBrush = new SolidColorBrush(
            darkCanvas ? Color.FromArgb(22, 130, 145, 165) : Color.FromArgb(26, 150, 138, 124));
        var originBrush = new SolidColorBrush(
            darkCanvas ? Color.FromArgb(70, 180, 195, 215) : Color.FromArgb(72, 90, 110, 130));

        void Add(UIElement el)
        {
            if (addChildZ != null)
                addChildZ(canvas, el, zIndex);
            else
                canvas.Children.Add(el);
        }

        var minorStep = spacing;
        var majorEvery = spacing >= 50 ? 2 : 5;
        var startX = Math.Floor(scene.MinX / minorStep) * minorStep;
        var startY = Math.Floor(scene.MinY / minorStep) * minorStep;
        var lineIndex = 0;

        for (var x = startX; x <= scene.MaxX + minorStep * 0.01; x += minorStep)
        {
            var isMajor = Math.Abs(x % (minorStep * majorEvery)) < 1e-6 || Math.Abs(x) < 1e-6;
            var brush = Math.Abs(x) < 1e-6 ? originBrush : isMajor ? majorBrush : minorBrush;
            var a = layout.WorldToCanvas((float)x, scene.MinY);
            var b = layout.WorldToCanvas((float)x, scene.MaxY);
            Add(new Line
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                Stroke = brush,
                StrokeThickness = isMajor ? 1.1 : 0.75,
            });
            lineIndex++;
        }

        for (var y = startY; y <= scene.MaxY + minorStep * 0.01; y += minorStep)
        {
            var isMajor = Math.Abs(y % (minorStep * majorEvery)) < 1e-6 || Math.Abs(y) < 1e-6;
            var brush = Math.Abs(y) < 1e-6 ? originBrush : isMajor ? majorBrush : minorBrush;
            var a = layout.WorldToCanvas(scene.MinX, (float)y);
            var b = layout.WorldToCanvas(scene.MaxX, (float)y);
            Add(new Line
            {
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                Stroke = brush,
                StrokeThickness = isMajor ? 1.1 : 0.75,
            });
            lineIndex++;
        }

        _ = lineIndex;
    }
}
