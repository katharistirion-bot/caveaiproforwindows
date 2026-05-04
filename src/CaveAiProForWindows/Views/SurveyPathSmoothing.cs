using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Converts polyline control points to smooth <see cref="PathGeometry"/> using Catmull-Rom
/// splines expressed as cubic <see cref="PolyBezierSegment"/> (chord-length knot spacing for
/// stable corners, with optional light Laplacian pre-smoothing on closed rings).
/// </summary>
internal static class SurveyPathSmoothing
{
    private static bool IsOk(Point p) =>
        !double.IsNaN(p.X) && !double.IsInfinity(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.Y);

    private static double DistSq(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static double Dist(Point a, Point b) => Math.Sqrt(DistSq(a, b));

    /// <summary>Remove consecutive points closer than <paramref name="minDist"/> (screen units).</summary>
    private static List<Point> RemoveNearDuplicates(IReadOnlyList<Point> pts, double minDist)
    {
        var minSq = minDist * minDist;
        var res = new List<Point>(pts.Count);
        foreach (var p in pts)
        {
            if (!IsOk(p))
                continue;
            if (res.Count == 0 || DistSq(res[^1], p) > minSq)
                res.Add(p);
        }

        return res;
    }

    /// <summary>Circular Laplacian — softens sharp LRUD joints with minimal shrinkage.</summary>
    private static void LaplacianSmoothClosedRing(IList<Point> ring, int passes, double lambda)
    {
        var n = ring.Count;
        if (n < 4 || passes <= 0 || lambda <= 0)
            return;
        var tmp = new Point[n];
        for (var pass = 0; pass < passes; pass++)
        {
            for (var i = 0; i < n; i++)
            {
                var im = ring[(i - 1 + n) % n];
                var ip = ring[(i + 1) % n];
                var cur = ring[i];
                tmp[i] = new Point(
                    (1 - lambda) * cur.X + lambda * 0.5 * (im.X + ip.X),
                    (1 - lambda) * cur.Y + lambda * 0.5 * (im.Y + ip.Y));
            }

            for (var i = 0; i < n; i++)
                ring[i] = tmp[i];
        }
    }

    /// <summary>Interior-only smoothing; endpoints stay fixed.</summary>
    private static void LaplacianSmoothOpenChain(IList<Point> chain, int passes, double lambda)
    {
        var n = chain.Count;
        if (n < 5 || passes <= 0 || lambda <= 0)
            return;
        var tmp = new Point[n];
        for (var pass = 0; pass < passes; pass++)
        {
            tmp[0] = chain[0];
            tmp[n - 1] = chain[n - 1];
            for (var i = 1; i < n - 1; i++)
            {
                var im = chain[i - 1];
                var ip = chain[i + 1];
                var cur = chain[i];
                tmp[i] = new Point(
                    (1 - lambda) * cur.X + lambda * 0.5 * (im.X + ip.X),
                    (1 - lambda) * cur.Y + lambda * 0.5 * (im.Y + ip.Y));
            }

            for (var i = 1; i < n - 1; i++)
                chain[i] = tmp[i];
        }
    }

    /// <summary>Chord-length–weighted Catmull-Rom → cubic Bezier control points (reduces overshoot vs uniform 1/6).</summary>
    private static void CatmullRomToBezier(Point p0, Point p1, Point p2, Point p3, out Point c1, out Point c2)
    {
        const double eps = 1e-7;
        var d1 = Math.Max(eps, Dist(p0, p1));
        var d2 = Math.Max(eps, Dist(p1, p2));
        var d3 = Math.Max(eps, Dist(p2, p3));
        var s1 = d2 / (3 * (d1 + d2));
        var s2 = d2 / (3 * (d2 + d3));
        c1 = new Point(
            p1.X + (p2.X - p0.X) * s1,
            p1.Y + (p2.Y - p0.Y) * s1);
        c2 = new Point(
            p2.X - (p3.X - p1.X) * s2,
            p2.Y - (p3.Y - p1.Y) * s2);
    }

    private static PathGeometry? BuildClosedFromProcessed(List<Point> pts)
    {
        var n = pts.Count;
        if (n < 3)
            return null;

        var polyPts = new PointCollection(n * 3);
        for (var i = 0; i < n; i++)
        {
            var p0 = pts[(i - 1 + n) % n];
            var p1 = pts[i];
            var p2 = pts[(i + 1) % n];
            var p3 = pts[(i + 2) % n];
            if (!IsOk(p0) || !IsOk(p1) || !IsOk(p2) || !IsOk(p3))
                return null;
            CatmullRomToBezier(p0, p1, p2, p3, out var c1, out var c2);
            polyPts.Add(c1);
            polyPts.Add(c2);
            polyPts.Add(p2);
        }

        var fig = new PathFigure(
            pts[0],
            new[] { new PolyBezierSegment(polyPts, true) },
            false);
        return new PathGeometry(new[] { fig }) { FillRule = FillRule.Nonzero };
    }

    /// <summary>Closed smooth loop; pre-smooths LRUD ribbon corners before spline fit.</summary>
    public static PathGeometry? TryBuildClosedCatmullRomPath(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 3)
            return null;
        var work = RemoveNearDuplicates(pts, 0.22);
        if (work.Count >= 6)
            LaplacianSmoothClosedRing(work, passes: 2, lambda: 0.14);
        work = RemoveNearDuplicates(work, 0.12);
        if (work.Count < 3)
            return null;
        return BuildClosedFromProcessed(work);
    }

    private static PathGeometry? BuildOpenFromProcessed(List<Point> pts)
    {
        var n = pts.Count;
        if (n < 2)
            return null;
        if (n == 2)
        {
            var fig2 = new PathFigure(pts[0], new PathSegment[] { new LineSegment(pts[1], true) }, false);
            return new PathGeometry(new[] { fig2 });
        }

        Point Pm1(int idx) => idx switch
        {
            -1 => new Point(2 * pts[0].X - pts[1].X, 2 * pts[0].Y - pts[1].Y),
            _ when idx >= n => new Point(2 * pts[n - 1].X - pts[n - 2].X, 2 * pts[n - 1].Y - pts[n - 2].Y),
            _ => pts[idx],
        };

        var polyPts = new PointCollection((n - 1) * 3);
        for (var i = 0; i < n - 1; i++)
        {
            var p0 = Pm1(i - 1);
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = Pm1(i + 2);
            if (!IsOk(p0) || !IsOk(p1) || !IsOk(p2) || !IsOk(p3))
                return null;
            CatmullRomToBezier(p0, p1, p2, p3, out var c1, out var c2);
            polyPts.Add(c1);
            polyPts.Add(c2);
            polyPts.Add(p2);
        }

        var fig = new PathFigure(pts[0], new[] { new PolyBezierSegment(polyPts, true) }, false) { IsClosed = false };
        return new PathGeometry(new[] { fig }) { FillRule = FillRule.Nonzero };
    }

    /// <summary>Open curve with mirrored endpoint tangents; uses chordal Bezier handles.</summary>
    public static PathGeometry? TryBuildOpenCatmullRomPath(IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2)
            return null;
        var work = RemoveNearDuplicates(pts, 0.22);
        if (work.Count >= 6)
            LaplacianSmoothOpenChain(work, passes: 1, lambda: 0.12);
        work = RemoveNearDuplicates(work, 0.12);
        if (work.Count < 2)
            return null;
        return BuildOpenFromProcessed(work);
    }
}
