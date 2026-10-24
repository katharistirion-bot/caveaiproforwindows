using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Slides a selected design-layer stroke or symbol without changing its shape.</summary>
public static class DesignLayerInkMover
{
    public static bool TryTranslate(UIElement element, double dx, double dy)
    {
        if (!DesignLayerInkHitTest.IsEditableInk(element))
            return false;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
            return false;

        switch (element)
        {
            case Polyline poly:
                var points = poly.Points;
                for (var i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    points[i] = new Point(p.X + dx, p.Y + dy);
                }

                return true;
            case Viewbox or TextBlock:
                var host = (FrameworkElement)element;
                var left = Canvas.GetLeft(host);
                var top = Canvas.GetTop(host);
                if (double.IsNaN(left))
                    left = 0;
                if (double.IsNaN(top))
                    top = 0;
                Canvas.SetLeft(host, left + dx);
                Canvas.SetTop(host, top + dy);
                return true;
            default:
                return false;
        }
    }
}
