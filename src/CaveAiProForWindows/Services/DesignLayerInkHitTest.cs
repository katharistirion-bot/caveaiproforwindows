using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>Stroke-level hit-testing for freehand polylines and symbol stamps on the design layer.</summary>
public static class DesignLayerInkHitTest
{
    public const string SelectionFrameTag = "__caveAiInkSelectionFrame";

    public static UIElement? FindTopmostHit(Canvas designLayer, Point canvasPoint, double toleranceDip = 10)
    {
        ArgumentNullException.ThrowIfNull(designLayer);
        var tol2 = toleranceDip * toleranceDip;
        UIElement? best = null;
        var bestZ = -1;

        for (var i = 0; i < designLayer.Children.Count; i++)
        {
            var child = designLayer.Children[i];
            if (!IsEditableInk(child))
                continue;
            if (!HitTest(child, canvasPoint, tol2))
                continue;

            if (i >= bestZ)
            {
                bestZ = i;
                best = child;
            }
        }

        return best;
    }

    public static bool IsEditableInk(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            if (Equals(fe.Tag, AndroidImportedSymbolPresenter.ImportedChildTag))
                return false;
            if (Equals(fe.Tag, SelectionFrameTag))
                return false;
        }

        return element switch
        {
            Polyline poly => poly.Points.Count >= 2,
            Viewbox => true,
            Ellipse => false,
            _ => false,
        };
    }

    public static Rect GetInkBounds(UIElement element, double paddingDip = 4)
    {
        switch (element)
        {
            case Polyline poly when poly.Points.Count > 0:
            {
                var minX = poly.Points[0].X;
                var maxX = minX;
                var minY = poly.Points[0].Y;
                var maxY = minY;
                for (var i = 1; i < poly.Points.Count; i++)
                {
                    var pt = poly.Points[i];
                    minX = Math.Min(minX, pt.X);
                    maxX = Math.Max(maxX, pt.X);
                    minY = Math.Min(minY, pt.Y);
                    maxY = Math.Max(maxY, pt.Y);
                }

                var half = Math.Max(paddingDip, poly.StrokeThickness * 0.5 + 2);
                return new Rect(minX - half, minY - half, maxX - minX + half * 2, maxY - minY + half * 2);
            }
            case Viewbox vb:
            {
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
                return new Rect(left - paddingDip, top - paddingDip, w + paddingDip * 2, h + paddingDip * 2);
            }
            default:
                return Rect.Empty;
        }
    }

    private static bool HitTest(UIElement element, Point pt, double toleranceSquared)
    {
        return element switch
        {
            Polyline poly => HitTestPolyline(poly, pt, toleranceSquared),
            Viewbox vb => HitTestViewbox(vb, pt, toleranceSquared),
            _ => false,
        };
    }

    private static bool HitTestPolyline(Polyline poly, Point pt, double toleranceSquared)
    {
        if (poly.Points.Count < 2)
            return false;

        var px = pt.X;
        var py = pt.Y;
        for (var i = 0; i < poly.Points.Count - 1; i++)
        {
            var a = poly.Points[i];
            var b = poly.Points[i + 1];
            if (SqDistPointSegment(px, py, a.X, a.Y, b.X, b.Y) <= toleranceSquared)
                return true;
        }

        return false;
    }

    private static bool HitTestViewbox(Viewbox vb, Point pt, double toleranceSquared)
    {
        var bounds = GetInkBounds(vb, 0);
        if (bounds.IsEmpty)
            return false;

        var cx = bounds.X + bounds.Width * 0.5;
        var cy = bounds.Y + bounds.Height * 0.5;
        var dx = pt.X - cx;
        var dy = pt.Y - cy;
        var halfW = bounds.Width * 0.5 + Math.Sqrt(toleranceSquared);
        var halfH = bounds.Height * 0.5 + Math.Sqrt(toleranceSquared);
        return Math.Abs(dx) <= halfW && Math.Abs(dy) <= halfH;
    }

    private static double SqDistPointSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lab2 = abx * abx + aby * aby;
        if (lab2 < 1e-14)
        {
            var dx0 = px - ax;
            var dy0 = py - ay;
            return dx0 * dx0 + dy0 * dy0;
        }

        var t = Math.Clamp(((px - ax) * abx + (py - ay) * aby) / lab2, 0d, 1d);
        var cx = ax + t * abx;
        var cy = ay + t * aby;
        var dx = px - cx;
        var dy = py - cy;
        return dx * dx + dy * dy;
    }
}
