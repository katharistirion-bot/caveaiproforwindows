using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Force-directed label displacement to resolve 2D bounding-box overlaps.
/// Works on screen-space rects after initial placement (complements <see cref="SurveyMapLabelLayout"/>).
/// </summary>
public static class SurveyMapSmartLabelLayout
{
    public sealed record LabelPlacement(
        string Id,
        Rect Bounds,
        Point Anchor,
        bool PinToAnchor = false,
        double Priority = 1.0);

    public sealed record SmartLabelLayoutResult(
        IReadOnlyList<LabelPlacement> Placements,
        int IterationsUsed,
        int OverlapCountRemaining);

    public const int DefaultMaxIterations = 32;

    public const double RepulsionStrength = 0.65;

    public const double AnchorSpring = 0.08;

    public const double MaxStepPx = 12.0;

    /// <summary>
    /// Iteratively pushes overlapping label boxes apart while tethering them to anchors with a weak spring.
    /// </summary>
    public static SmartLabelLayoutResult Resolve(
        IReadOnlyList<LabelPlacement> initial,
        int maxIterations = DefaultMaxIterations)
    {
        if (initial.Count <= 1)
            return new SmartLabelLayoutResult(initial, 0, 0);

        var boxes = initial.Select(p => p with { Bounds = p.Bounds }).ToList();
        var iterUsed = 0;

        for (var iter = 0; iter < maxIterations; iter++)
        {
            iterUsed = iter + 1;
            var moved = false;

            for (var i = 0; i < boxes.Count; i++)
            {
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    if (!boxes[i].Bounds.IntersectsWith(boxes[j].Bounds))
                        continue;

                    var mtv = MinimumTranslation(boxes[i].Bounds, boxes[j].Bounds);
                    if (mtv.LengthSquared < 1e-6)
                        mtv = new Vector(1, 0);

                    var push = mtv * (RepulsionStrength * 0.5);
                    if (!boxes[i].PinToAnchor)
                        boxes[i] = Shift(boxes[i], -push, boxes[i].Priority);
                    if (!boxes[j].PinToAnchor)
                        boxes[j] = Shift(boxes[j], push, boxes[j].Priority);
                    moved = true;
                }
            }

            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].PinToAnchor)
                    continue;
                var toAnchor = boxes[i].Anchor - boxes[i].Bounds.TopLeft;
                var spring = new Vector(
                    ClampStep(toAnchor.X * AnchorSpring),
                    ClampStep(toAnchor.Y * AnchorSpring));
                if (spring.LengthSquared > 1e-6)
                {
                    boxes[i] = Shift(boxes[i], spring, boxes[i].Priority);
                    moved = true;
                }
            }

            if (!moved)
                break;
        }

        var overlaps = CountOverlaps(boxes);
        return new SmartLabelLayoutResult(boxes, iterUsed, overlaps);
    }

    /// <summary>Builds label boxes from station name anchors for smart layout pass.</summary>
    public static IReadOnlyList<LabelPlacement> FromStationAnchors(
        IEnumerable<(string Name, Point Anchor)> stations,
        double widthPx = 58,
        double heightPx = 24,
        bool pinEndpoints = true)
    {
        var list = stations.ToList();
        var result = new List<LabelPlacement>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            var (name, anchor) = list[i];
            var bounds = new Rect(anchor.X, anchor.Y, widthPx, heightPx);
            var pin = pinEndpoints && (i == 0 || i == list.Count - 1);
            result.Add(new LabelPlacement(name, bounds, anchor, pin, Priority: pin ? 2.0 : 1.0));
        }

        return result;
    }

    private static LabelPlacement Shift(LabelPlacement p, Vector delta, double priority)
    {
        var scale = 1.0 / Math.Max(0.5, priority);
        delta = new Vector(ClampStep(delta.X * scale), ClampStep(delta.Y * scale));
        var b = p.Bounds;
        b.X += delta.X;
        b.Y += delta.Y;
        return p with { Bounds = b };
    }

    private static Vector MinimumTranslation(Rect a, Rect b)
    {
        var overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var overlapY = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        if (overlapX <= 0 || overlapY <= 0)
            return new Vector();

        if (overlapX < overlapY)
        {
            var dir = a.Left + a.Width * 0.5 < b.Left + b.Width * 0.5 ? -1.0 : 1.0;
            return new Vector(dir * overlapX, 0);
        }

        var dirY = a.Top + a.Height * 0.5 < b.Top + b.Height * 0.5 ? -1.0 : 1.0;
        return new Vector(0, dirY * overlapY);
    }

    private static double ClampStep(double v) => Math.Clamp(v, -MaxStepPx, MaxStepPx);

    private static int CountOverlaps(IReadOnlyList<LabelPlacement> boxes)
    {
        var n = 0;
        for (var i = 0; i < boxes.Count; i++)
        for (var j = i + 1; j < boxes.Count; j++)
        {
            if (boxes[i].Bounds.IntersectsWith(boxes[j].Bounds))
                n++;
        }

        return n;
    }
}
