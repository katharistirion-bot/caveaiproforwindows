using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Converts polyline control points to smooth <see cref="PathGeometry"/> using uniform Catmull-Rom
/// knots expressed as cubic <see cref="BezierSegment"/> (standard 1/6 factor).
/// </summary>
internal static class SurveyPathSmoothing
{
    private const double Eps = 1e-9;

    private static bool IsOk(Point p) =>
        !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y);

    /// <summary>Closed smooth loop; requires at least 4 distinct points for a pleasant Catmull-Rom ring.</summary>
    public static PathGeometry? TryBuildClosedCatmullRomPath(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 3)
            return null;

        if (pts.Count == 3)
            {
                var fig3 = new PathFigure(pts[0], new PathSegment[]
                {
                    new LineSegment(pts[1], true),
                    new LineSegment(pts[2], true),
                }, true);
                return new PathGeometry(new[] { fig3 });
            }

        var n = pts.Count;
        var segments = new List<PathSegment>(n);
        for (var i = 0; i < n; i++)
        {
            var p0 = pts[(i - 1 + n) % n];
            var p1 = pts[i];
            var p2 = pts[(i + 1) % n];
            var p3 = pts[(i + 2) % n];
            if (!IsOk(p0) || !IsOk(p1) || !IsOk(p2) || !IsOk(p3))
                continue;
            var c1 = new Point(
                p1.X + (p2.X - p0.X) / 6.0,
                p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(
                p2.X - (p3.X - p1.X) / 6.0,
                p2.Y - (p3.Y - p1.Y) / 6.0);
            segments.Add(new BezierSegment(c1, c2, p2, true));
        }

        if (segments.Count == 0)
            return null;

        // Last Bezier already ends at pts[0] == StartPoint; do not set IsClosed (avoids an extra closing edge).
        var fig = new PathFigure(pts[0], segments, false);
        return new PathGeometry(new[] { fig }) { FillRule = FillRule.Nonzero };
    }

    /// <summary>Open curve with natural end tangents (mirrored endpoints).</summary>
    public static PathGeometry? TryBuildOpenCatmullRomPath(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2)
            return null;

        if (pts.Count == 2)
        {
            var fig2 = new PathFigure(pts[0], new PathSegment[] { new LineSegment(pts[1], true) }, false);
            return new PathGeometry(new[] { fig2 });
        }

        var n = pts.Count;
        Point Pm1(int idx) => idx switch
        {
            -1 => new Point(2 * pts[0].X - pts[1].X, 2 * pts[0].Y - pts[1].Y),
            _ when idx >= n => new Point(2 * pts[n - 1].X - pts[n - 2].X, 2 * pts[n - 1].Y - pts[n - 2].Y),
            _ => pts[idx],
        };

        var segments = new List<PathSegment>(n - 1);
        for (var i = 0; i < n - 1; i++)
        {
            var p0 = Pm1(i - 1);
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = Pm1(i + 2);
            if (!IsOk(p0) || !IsOk(p1) || !IsOk(p2) || !IsOk(p3))
                continue;
            var c1 = new Point(
                p1.X + (p2.X - p0.X) / 6.0,
                p1.Y + (p2.Y - p0.Y) / 6.0);
            var c2 = new Point(
                p2.X - (p3.X - p1.X) / 6.0,
                p2.Y - (p3.Y - p1.Y) / 6.0);
            segments.Add(new BezierSegment(c1, c2, p2, true));
        }

        if (segments.Count == 0)
            return null;

        var fig = new PathFigure(pts[0], segments, false) { IsClosed = false };
        return new PathGeometry(new[] { fig }) { FillRule = FillRule.Nonzero };
    }

    /// <summary>Approximate length of first segment — used to skip micro loops.</summary>
    public static double MinChordLength(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2)
            return 0;
        var dx = pts[1].X - pts[0].X;
        var dy = pts[1].Y - pts[0].Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static bool IsDegenerate(IReadOnlyList<Point> pts, double minChordPx)
    {
        if (pts.Count < 2)
            return true;
        if (minChordPx <= Eps)
            return false;
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var dx = pts[i + 1].X - pts[i].X;
            var dy = pts[i + 1].Y - pts[i].Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= minChordPx)
                return false;
        }

        return true;
    }
}
