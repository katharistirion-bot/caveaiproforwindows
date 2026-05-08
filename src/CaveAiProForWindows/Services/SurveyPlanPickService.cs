using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using CaveAiProForWindows.Models;
using StationCoords = CaveAiProForWindows.Services.SurveyStationGeometry.StationPlanCoords;

namespace CaveAiProForWindows.Services;

public abstract record SurveyPickResult;

public sealed record SurveyPickStation(string Name, StationCoords Coord) : SurveyPickResult;

public sealed record SurveyPickLeg(ShotRecord Shot, StationCoords FromCoord, StationCoords ToCoord) : SurveyPickResult;

public sealed record SurveyPickNone : SurveyPickResult;

/// <summary>Closest station vs traverse leg hit test in survey plan canvas space.</summary>
public static class SurveyPlanPickService
{
    /// <summary>Generous slop so stations remain easy targets in the broad pick (Select mode).</summary>
    private const double SelectModeStationRadiusDip = 14;

    /// <summary>Tighter slop for "always-on" station picking in PanZoom — only fires when the user clicks the visible marker.</summary>
    private const double PanZoomStationRadiusDip = 11;

    private const double LegRadiusDip = 6;

    /// <summary>
    /// <paramref name="canvasPoint"/> — position in the same DIP space as survey vectors (inside <see cref="Canvas"/> sized to tile).
    /// </summary>
    public static SurveyPickResult TryPick(
        Point canvasPoint,
        PlanCanvasSurveyLayout layout,
        PlanScene scene,
        CaveProjectDocument project)
    {
        if (scene.Stations.Count == 0 || layout.Scale <= 1e-12)
            return new SurveyPickNone();

        var (wx, wy) = layout.CanvasToWorld(canvasPoint.X, canvasPoint.Y);

        var rs = Math.Max(0.45, SelectModeStationRadiusDip / layout.Scale);
        var rl = Math.Max(0.45, LegRadiusDip / layout.Scale);

        double bestStD = double.MaxValue;
        SurveyPickStation? bestSt = null;
        foreach (var kv in scene.Stations)
        {
            var c = kv.Value;
            var d = Math.Sqrt(SqDist(wx, wy, c.X, c.Y));
            if (d < bestStD)
            {
                bestStD = d;
                bestSt = new SurveyPickStation(kv.Key, c);
            }
        }

        double bestLegD = double.MaxValue;
        SurveyPickLeg? bestLeg = null;
        foreach (var leg in EnumerateLegs(scene, project))
        {
            var d = Math.Sqrt(SqDistPointSegment(wx, wy, leg.A.X, leg.A.Y, leg.B.X, leg.B.Y));
            if (d < bestLegD)
            {
                bestLegD = d;
                bestLeg = new SurveyPickLeg(leg.Shot, leg.A, leg.B);
            }
        }

        if (bestSt != null && bestStD <= rs && bestStD <= bestLegD)
            return bestSt;

        if (bestLeg != null && bestLegD <= rl)
            return bestLeg;

        return new SurveyPickNone();
    }

    /// <summary>
    /// Station-only hit test — used in PanZoom mode so left-click on a marker selects without preempting drag-pan
    /// on empty space. Returns null when no station is within the tight pick radius.
    /// </summary>
    public static SurveyPickStation? TryPickStation(
        Point canvasPoint,
        PlanCanvasSurveyLayout layout,
        PlanScene scene)
        => TryPickStation(canvasPoint, layout, scene, PanZoomStationRadiusDip);

    /// <summary>
    /// Station-only hit test with explicit DIP radius (canvas-space). The radius scales inversely with
    /// <see cref="PlanCanvasSurveyLayout.Scale"/> so the world-radius shrinks as the projection zooms in.
    /// </summary>
    public static SurveyPickStation? TryPickStation(
        Point canvasPoint,
        PlanCanvasSurveyLayout layout,
        PlanScene scene,
        double pickRadiusDip)
    {
        if (scene.Stations.Count == 0 || layout.Scale <= 1e-12)
            return null;

        var radius = Math.Max(0.45, pickRadiusDip / layout.Scale);
        var (wx, wy) = layout.CanvasToWorld(canvasPoint.X, canvasPoint.Y);

        double bestD = double.MaxValue;
        SurveyPickStation? best = null;
        foreach (var kv in scene.Stations)
        {
            var c = kv.Value;
            var d = Math.Sqrt(SqDist(wx, wy, c.X, c.Y));
            if (d < bestD)
            {
                bestD = d;
                best = new SurveyPickStation(kv.Key, c);
            }
        }

        return bestD <= radius ? best : null;
    }

    private static IEnumerable<(ShotRecord Shot, StationCoords A, StationCoords B)> EnumerateLegs(
        PlanScene scene,
        CaveProjectDocument project)
    {
        foreach (var s in project.Shots.Where(x => x.IsTraverseLeg))
        {
            if (!TryGetStation(scene.Stations, s.FromStation, out var a))
                continue;
            if (!TryGetStation(scene.Stations, s.ToStation, out var b))
                continue;
            yield return (s, a, b);
        }
    }

    private static bool TryGetStation(
        IReadOnlyDictionary<string, StationCoords> stations,
        string name,
        out StationCoords coord)
    {
        var n = (name ?? "").Trim();
        if (stations.TryGetValue(n, out coord!))
            return true;
        var match = stations.Keys.FirstOrDefault(
            k => string.Equals(k.Trim(), n, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return stations.TryGetValue(match, out coord!);
        coord = new StationCoords("?", float.NaN, float.NaN, float.NaN);
        return false;
    }

    private static double SqDist(double px, double py, float ax, float ay)
    {
        var dx = px - ax;
        var dy = py - ay;
        return dx * dx + dy * dy;
    }

    private static double SqDistPointSegment(double px, double py, float ax, float ay, float bx, float by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lab2 = abx * abx + aby * aby;
        if (lab2 < 1e-14)
            return SqDist(px, py, ax, ay);

        var t = Math.Clamp(((px - ax) * abx + (py - ay) * aby) / lab2, 0d, 1d);
        var cx = ax + t * abx;
        var cy = ay + t * aby;
        var dx2 = px - cx;
        var dy2 = py - cy;
        return dx2 * dx2 + dy2 * dy2;
    }
}
