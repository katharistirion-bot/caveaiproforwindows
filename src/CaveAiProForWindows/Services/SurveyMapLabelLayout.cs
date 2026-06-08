using System.Windows;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Label sets after shared screen-space thinning (all 2D map tabs).</summary>
public sealed record SurveyMapLabelLayoutResult(
    IReadOnlyList<SurveyStationGeometry.StationPlanCoords> StationNames,
    IReadOnlyList<SurveyStationGeometry.StationPlanCoords> StationZ,
    IReadOnlyList<SurveyLegMapLabel> Legs,
    IReadOnlyList<SurveyStationEnvMapLabel> Environment);

/// <summary>Screen-space thinning so dense traverses (many short legs) stay readable at fit zoom.</summary>
public static class SurveyMapLabelLayout
{
    /// <summary>Below this px/m, leg chips and environment pills are hidden (on-screen LOD only).</summary>
    public const double LegEnvironmentLodThresholdPxPerMetre = 8.0;

    /// <summary>Below this px/m, station Z labels are reduced to traverse ends only.</summary>
    public const double StationZLodThresholdPxPerMetre = 4.0;

    private const double LegChipWidthPx = 76;
    private const double LegChipHeightPx = 54;
    private const double NameChipWidthPx = 58;
    private const double NameChipHeightPx = 24;
    private const double ZChipWidthPx = 52;
    private const double ZChipHeightPx = 20;
    private const double MinLegGapPx = 46;
    private const double MinStationGapPx = 40;

    /// <summary>One shared layout pass for station names, Z, leg chips, and environment pills.</summary>
    public static SurveyMapLabelLayoutResult Resolve(
        CaveProjectDocument? project,
        PlanScene scene,
        SurveyMapAnnotations annotations,
        PlanCanvasDrawOptions opt,
        Func<float, float, Point> toScreen,
        double pxPerMetre)
    {
        var ordered = OrderStations(project, scene);
        var occupancy = new List<Rect>();

        var showLegs = opt.ShowLegSurveyDetails && pxPerMetre >= LegEnvironmentLodThresholdPxPerMetre;
        var showEnv = opt.ShowStationEnvironment && pxPerMetre >= LegEnvironmentLodThresholdPxPerMetre;
        var showStationZ = opt.ShowStationZDepth;
        var stationZSource = ordered;
        if (showStationZ && pxPerMetre < StationZLodThresholdPxPerMetre && ordered.Count > 2)
            stationZSource = new[] { ordered[0], ordered[^1] };

        var stationNames = opt.ShowStationNames
            ? FilterStationNames(ordered, toScreen, pxPerMetre, occupancy)
            : Array.Empty<SurveyStationGeometry.StationPlanCoords>();

        var stationZ = showStationZ
            ? FilterStationZ(stationZSource, toScreen, pxPerMetre, occupancy, opt.ShowStationNames)
            : Array.Empty<SurveyStationGeometry.StationPlanCoords>();

        var legs = showLegs
            ? FilterLegLabels(annotations.LegLabels, toScreen, pxPerMetre, occupancy)
            : Array.Empty<SurveyLegMapLabel>();

        var env = showEnv
            ? FilterStationEnvironment(
                annotations.StationEnvironment,
                toScreen,
                opt.ShowStationNames,
                showStationZ,
                occupancy)
            : Array.Empty<SurveyStationEnvMapLabel>();

        return new SurveyMapLabelLayoutResult(stationNames, stationZ, legs, env);
    }

    public static IReadOnlyList<SurveyLegMapLabel> FilterLegLabels(
        IReadOnlyList<SurveyLegMapLabel> legs,
        Func<float, float, Point> toScreen,
        double pxPerMetre) =>
        FilterLegLabels(legs, toScreen, pxPerMetre, new List<Rect>());

    public static IReadOnlyList<SurveyStationEnvMapLabel> FilterStationEnvironment(
        IReadOnlyList<SurveyStationEnvMapLabel> labels,
        Func<float, float, Point> toScreen,
        bool stationNamesVisible,
        bool stationZVisible) =>
        FilterStationEnvironment(labels, toScreen, stationNamesVisible, stationZVisible, new List<Rect>());

    private static IReadOnlyList<SurveyStationGeometry.StationPlanCoords> FilterStationNames(
        IReadOnlyList<SurveyStationGeometry.StationPlanCoords> ordered,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        List<Rect> occupancy)
    {
        if (ordered.Count <= 1)
            return ordered;

        var minGap = StationMinGap(pxPerMetre, ordered.Count);
        var kept = new List<SurveyStationGeometry.StationPlanCoords>();
        Point? lastAnchor = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var isEnd = i == 0 || i == ordered.Count - 1;
            var c = ordered[i];
            var anchor = NameAnchor(c, toScreen);

            if (!isEnd && lastAnchor is { } prev && (anchor - prev).Length < minGap)
                continue;

            var rect = NameRect(anchor);
            if (!isEnd && Overlaps(rect, occupancy))
                continue;

            kept.Add(c);
            lastAnchor = anchor;
            occupancy.Add(rect);
        }

        return kept;
    }

    private static IReadOnlyList<SurveyStationGeometry.StationPlanCoords> FilterStationZ(
        IReadOnlyList<SurveyStationGeometry.StationPlanCoords> ordered,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        List<Rect> occupancy,
        bool stationNamesVisible)
    {
        if (ordered.Count <= 1)
            return ordered;

        var minGap = StationMinGap(pxPerMetre, ordered.Count);
        var kept = new List<SurveyStationGeometry.StationPlanCoords>();
        Point? lastAnchor = null;

        for (var i = 0; i < ordered.Count; i++)
        {
            var isEnd = i == 0 || i == ordered.Count - 1;
            var c = ordered[i];
            var anchor = ZAnchor(c, toScreen, stationNamesVisible);

            if (!isEnd && lastAnchor is { } prev && (anchor - prev).Length < minGap)
                continue;

            var rect = ZRect(anchor);
            if (!isEnd && Overlaps(rect, occupancy))
                continue;

            kept.Add(c);
            lastAnchor = anchor;
            occupancy.Add(rect);
        }

        return kept;
    }

    private static IReadOnlyList<SurveyLegMapLabel> FilterLegLabels(
        IReadOnlyList<SurveyLegMapLabel> legs,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        List<Rect> occupancy)
    {
        if (legs.Count <= 1)
            return legs;

        var minGap = Math.Clamp(MinLegGapPx, pxPerMetre * 3.2, 120);
        var kept = new List<SurveyLegMapLabel>();
        Point? lastAnchor = null;

        for (var i = 0; i < legs.Count; i++)
        {
            var isEnd = i == 0 || i == legs.Count - 1;
            var leg = legs[i];
            var anchor = LegAnchor(leg, toScreen, pxPerMetre);

            if (!isEnd && lastAnchor is { } prev && (anchor - prev).Length < minGap)
                continue;

            if (!TryPlaceLeg(ref leg, anchor, occupancy, toScreen, pxPerMetre, isEnd))
                continue;

            anchor = LegAnchor(leg, toScreen, pxPerMetre);
            kept.Add(leg);
            lastAnchor = anchor;
            occupancy.Add(LegRect(anchor));
        }

        return kept;
    }

    private static IReadOnlyList<SurveyStationEnvMapLabel> FilterStationEnvironment(
        IReadOnlyList<SurveyStationEnvMapLabel> labels,
        Func<float, float, Point> toScreen,
        bool stationNamesVisible,
        bool stationZVisible,
        List<Rect> occupancy)
    {
        if (labels.Count <= 1)
            return labels;

        var yBump = 8.0 + (stationNamesVisible ? 18 : 0) + (stationZVisible ? 16 : 0);
        const double minGapPx = 52;
        var kept = new List<SurveyStationEnvMapLabel>();
        Point? lastAnchor = null;

        foreach (var env in labels)
        {
            var anchor = toScreen(env.X, env.Y);
            anchor = new Point(anchor.X + 6, anchor.Y + yBump);
            if (lastAnchor is { } prev && (anchor - prev).Length < minGapPx)
                continue;

            var rect = LegRect(anchor);
            if (Overlaps(rect, occupancy))
                continue;

            kept.Add(env);
            lastAnchor = anchor;
            occupancy.Add(rect);
        }

        return kept;
    }

    private static double StationMinGap(double pxPerMetre, int stationCount)
    {
        var baseGap = Math.Clamp(MinStationGapPx, pxPerMetre * 3.0, 96);
        if (stationCount > 60)
            return Math.Max(baseGap, 52);
        if (stationCount > 30)
            return Math.Max(baseGap, 44);
        return baseGap;
    }

    private static IReadOnlyList<SurveyStationGeometry.StationPlanCoords> OrderStations(
        CaveProjectDocument? project,
        PlanScene scene)
    {
        if (scene.Stations.Count == 0)
            return Array.Empty<SurveyStationGeometry.StationPlanCoords>();

        if (project != null && project.Shots.Count > 0)
        {
            var graph = SurveyTraverseGraph.BuildAdjacency(project.Shots);
            if (graph.Count > 0)
            {
                var root = SurveyTraverseGraph.FirstRootStation(graph);
                var chainage = SurveyTraverseGraph.DijkstraChainage(graph, root);
                return scene.Stations.Values
                    .Where(c => chainage.ContainsKey(c.Name))
                    .OrderBy(c => chainage[c.Name])
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return scene.Stations.Values
            .OrderBy(c => c.X)
            .ThenBy(c => c.Y)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool TryPlaceLeg(
        ref SurveyLegMapLabel leg,
        Point anchor,
        List<Rect> occupancy,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool force)
    {
        if (!Overlaps(LegRect(anchor), occupancy))
            return true;

        var flipped = leg with
        {
            PerpOffsetX = -leg.PerpOffsetX,
            PerpOffsetY = -leg.PerpOffsetY,
        };
        var flippedAnchor = LegAnchor(flipped, toScreen, pxPerMetre);
        if (!Overlaps(LegRect(flippedAnchor), occupancy))
        {
            leg = flipped;
            return true;
        }

        return force;
    }

    private static Point NameAnchor(SurveyStationGeometry.StationPlanCoords c, Func<float, float, Point> toScreen)
    {
        var pt = toScreen(c.X, c.Y);
        return new Point(pt.X + 4, pt.Y - 20);
    }

    private static Point ZAnchor(
        SurveyStationGeometry.StationPlanCoords c,
        Func<float, float, Point> toScreen,
        bool stationNamesVisible)
    {
        var pt = toScreen(c.X, c.Y);
        return new Point(pt.X + 4, pt.Y - (stationNamesVisible ? 2 : 16));
    }

    private static Point LegAnchor(SurveyLegMapLabel leg, Func<float, float, Point> toScreen, double pxPerMetre)
    {
        var pt = toScreen(leg.MidX, leg.MidY);
        return new Point(
            pt.X + leg.PerpOffsetX * pxPerMetre,
            pt.Y - leg.PerpOffsetY * pxPerMetre);
    }

    private static Rect NameRect(Point anchor) =>
        new(anchor.X, anchor.Y, NameChipWidthPx, NameChipHeightPx);

    private static Rect ZRect(Point anchor) =>
        new(anchor.X, anchor.Y, ZChipWidthPx, ZChipHeightPx);

    private static Rect LegRect(Point anchor) =>
        new(anchor.X - LegChipWidthPx * 0.5, anchor.Y - LegChipHeightPx * 0.5, LegChipWidthPx, LegChipHeightPx);

    private static bool Overlaps(Rect rect, IReadOnlyList<Rect> boxes)
    {
        foreach (var b in boxes)
        {
            if (rect.IntersectsWith(b))
                return true;
        }

        return false;
    }
}
