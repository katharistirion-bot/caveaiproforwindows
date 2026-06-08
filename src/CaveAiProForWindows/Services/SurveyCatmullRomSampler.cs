using System;
using System.Collections.Generic;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Catmull-Rom spline resampling for plan/profile LRUD wall rails at geometry build time
/// (survey metres). Complements render-time smoothing in <see cref="Views.SurveyPathSmoothing"/>.
/// </summary>
public static class SurveyCatmullRomSampler
{
    private const double Eps = 1e-7;

    /// <summary>Resample an open chain with chord-length Catmull-Rom segments.</summary>
    public static List<(float x, float y)> SampleOpenPlanChain(
        IReadOnlyList<(float x, float y)> chain,
        float targetSpacingM = 0.15f,
        int minSamplesPerSegment = 4,
        int maxSamplesPerSegment = 24)
    {
        if (chain.Count <= 1)
            return new List<(float x, float y)>(chain);
        if (chain.Count == 2)
            return DensifyOpen(chain, targetSpacingM);

        var work = RemoveNearDuplicates(chain, 0.08f);
        if (work.Count >= 6)
            LaplacianSmoothOpen(work, passes: 1, lambda: 0.12);
        work = RemoveNearDuplicates(work, 0.05f);
        if (work.Count < 2)
            return new List<(float x, float y)>(chain);

        if (work.Count == 2)
            return DensifyOpen(work, targetSpacingM);

        var res = new List<(float x, float y)>(work.Count * minSamplesPerSegment) { work[0] };
        var n = work.Count;
        for (var i = 0; i < n - 1; i++)
        {
            var p0 = Pm1(work, i - 1, n);
            var p1 = work[i];
            var p2 = work[i + 1];
            var p3 = Pm1(work, i + 2, n);
            var segLen = Dist(p1, p2);
            var samples = Math.Clamp(
                (int)Math.Ceiling(segLen / Math.Max(targetSpacingM, 0.04f)),
                minSamplesPerSegment,
                maxSamplesPerSegment);
            for (var k = 1; k <= samples; k++)
            {
                var t = k / (double)samples;
                var pt = EvaluateCatmullRom(p0, p1, p2, p3, t);
                AppendIfDistinct(res, pt, 0.04f);
            }
        }

        return res.Count >= 2 ? res : new List<(float x, float y)>(chain);
    }

    /// <summary>Resample a closed ring (first point not repeated at end).</summary>
    public static List<(float x, float y)> SampleClosedPlanChain(
        IReadOnlyList<(float x, float y)> ring,
        float targetSpacingM = 0.15f,
        int minSamplesPerSegment = 4,
        int maxSamplesPerSegment = 24)
    {
        if (ring.Count < 3)
            return new List<(float x, float y)>(ring);

        var work = RemoveNearDuplicates(ring, 0.08f);
        if (work.Count >= 6)
            LaplacianSmoothClosed(work, passes: 2, lambda: 0.14);
        work = RemoveNearDuplicates(work, 0.05f);
        if (work.Count < 3)
            return new List<(float x, float y)>(ring);

        var n = work.Count;
        var res = new List<(float x, float y)>(n * minSamplesPerSegment);
        for (var i = 0; i < n; i++)
        {
            var p0 = work[(i - 1 + n) % n];
            var p1 = work[i];
            var p2 = work[(i + 1) % n];
            var p3 = work[(i + 2) % n];
            var segLen = Dist(p1, p2);
            var samples = Math.Clamp(
                (int)Math.Ceiling(segLen / Math.Max(targetSpacingM, 0.04f)),
                minSamplesPerSegment,
                maxSamplesPerSegment);
            for (var k = 0; k < samples; k++)
            {
                var t = k / (double)samples;
                var pt = EvaluateCatmullRom(p0, p1, p2, p3, t);
                AppendIfDistinct(res, pt, 0.04f);
            }
        }

        return res.Count >= 3 ? res : new List<(float x, float y)>(ring);
    }

    private static (float x, float y) Pm1(IReadOnlyList<(float x, float y)> pts, int idx, int n) =>
        idx switch
        {
            -1 => ExtrapolateEnd(pts[0], pts[1]),
            _ when idx >= n => ExtrapolateEnd(pts[n - 1], pts[n - 2]),
            _ => pts[idx],
        };

    private static (float x, float y) ExtrapolateEnd((float x, float y) a, (float x, float y) b) =>
        (2 * a.x - b.x, 2 * a.y - b.y);

    private static (float x, float y) EvaluateCatmullRom(
        (float x, float y) p0,
        (float x, float y) p1,
        (float x, float y) p2,
        (float x, float y) p3,
        double t)
    {
        CatmullRomToBezier(p0, p1, p2, p3, out var c1, out var c2);
        var u = 1 - t;
        var x = u * u * u * p1.x + 3 * u * u * t * c1.x + 3 * u * t * t * c2.x + t * t * t * p2.x;
        var y = u * u * u * p1.y + 3 * u * u * t * c1.y + 3 * u * t * t * c2.y + t * t * t * p2.y;
        return ((float)x, (float)y);
    }

    private static void CatmullRomToBezier(
        (float x, float y) p0,
        (float x, float y) p1,
        (float x, float y) p2,
        (float x, float y) p3,
        out (float x, float y) c1,
        out (float x, float y) c2)
    {
        var d1 = Math.Max(Eps, Dist(p0, p1));
        var d2 = Math.Max(Eps, Dist(p1, p2));
        var d3 = Math.Max(Eps, Dist(p2, p3));
        var s1 = d2 / (3 * (d1 + d2));
        var s2 = d2 / (3 * (d2 + d3));
        c1 = ((float)(p1.x + (p2.x - p0.x) * s1), (float)(p1.y + (p2.y - p0.y) * s1));
        c2 = ((float)(p2.x - (p3.x - p1.x) * s2), (float)(p2.y - (p3.y - p1.y) * s2));
    }

    private static double Dist((float x, float y) a, (float x, float y) b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static List<(float x, float y)> RemoveNearDuplicates(IReadOnlyList<(float x, float y)> pts, float minDist)
    {
        var minSq = minDist * minDist;
        var res = new List<(float x, float y)>(pts.Count);
        foreach (var p in pts)
        {
            if (res.Count == 0)
            {
                res.Add(p);
                continue;
            }

            var last = res[^1];
            var dx = p.x - last.x;
            var dy = p.y - last.y;
            if (dx * dx + dy * dy > minSq)
                res.Add(p);
        }

        return res;
    }

    private static void AppendIfDistinct(List<(float x, float y)> chain, (float x, float y) p, float minDist)
    {
        if (chain.Count == 0)
        {
            chain.Add(p);
            return;
        }

        var last = chain[^1];
        var dx = p.x - last.x;
        var dy = p.y - last.y;
        if (dx * dx + dy * dy >= minDist * minDist)
            chain.Add(p);
    }

    private static void LaplacianSmoothClosed(IList<(float x, float y)> ring, int passes, double lambda)
    {
        var n = ring.Count;
        if (n < 4 || passes <= 0 || lambda <= 0)
            return;
        var tmp = new (float x, float y)[n];
        for (var pass = 0; pass < passes; pass++)
        {
            for (var i = 0; i < n; i++)
            {
                var im = ring[(i - 1 + n) % n];
                var ip = ring[(i + 1) % n];
                var cur = ring[i];
                tmp[i] = (
                    (float)((1 - lambda) * cur.x + lambda * 0.5 * (im.x + ip.x)),
                    (float)((1 - lambda) * cur.y + lambda * 0.5 * (im.y + ip.y)));
            }

            for (var i = 0; i < n; i++)
                ring[i] = tmp[i];
        }
    }

    private static void LaplacianSmoothOpen(IList<(float x, float y)> chain, int passes, double lambda)
    {
        var n = chain.Count;
        if (n < 5 || passes <= 0 || lambda <= 0)
            return;
        var tmp = new (float x, float y)[n];
        for (var pass = 0; pass < passes; pass++)
        {
            tmp[0] = chain[0];
            tmp[n - 1] = chain[n - 1];
            for (var i = 1; i < n - 1; i++)
            {
                var im = chain[i - 1];
                var ip = chain[i + 1];
                var cur = chain[i];
                tmp[i] = (
                    (float)((1 - lambda) * cur.x + lambda * 0.5 * (im.x + ip.x)),
                    (float)((1 - lambda) * cur.y + lambda * 0.5 * (im.y + ip.y)));
            }

            for (var i = 1; i < n - 1; i++)
                chain[i] = tmp[i];
        }
    }

    private static List<(float x, float y)> DensifyOpen(IReadOnlyList<(float x, float y)> chain, float stepM)
    {
        if (chain.Count <= 1)
            return new List<(float x, float y)>(chain);
        var res = new List<(float x, float y)> { chain[0] };
        for (var i = 0; i < chain.Count - 1; i++)
        {
            var p0 = chain[i];
            var p1 = chain[i + 1];
            var dx = p1.x - p0.x;
            var dy = p1.y - p0.y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-4)
            {
                res.Add(p1);
                continue;
            }

            var n = Math.Min(64, Math.Max(1, (int)Math.Ceiling(d / stepM)));
            for (var k = 1; k < n; k++)
            {
                var t = k / (float)n;
                res.Add((p0.x + t * dx, p0.y + t * dy));
            }

            res.Add(p1);
        }

        return res;
    }
}
