using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Screen-space picking for the pseudo-3D survey viewport.</summary>
public static class CaveViewport3DPickService
{
    public static SurveyPickResult? TryPick(
        Viewport3D viewport,
        Point mouse,
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        double stationRadiusPx = 16,
        double legRadiusPx = 10)
    {
        if (coords.Count == 0)
            return null;

        string? bestStation = null;
        var bestDist = double.MaxValue;
        foreach (var kv in coords)
        {
            var c = kv.Value;
            if (!Viewport3DProjection.TryWorldToScreen(viewport, new Point3D(c.X, c.Y, c.Z), out var sc))
                continue;
            var d = (sc - mouse).Length;
            if (d <= stationRadiusPx && d < bestDist)
            {
                bestDist = d;
                bestStation = kv.Key;
            }
        }

        if (bestStation != null && coords.TryGetValue(bestStation, out var picked))
            return new SurveyPickStation(bestStation, picked);

        SurveyPickLeg? bestLeg = null;
        bestDist = double.MaxValue;
        foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
        {
            var from = (shot.FromStation ?? "").Trim();
            var to = (shot.ToStation ?? "").Trim();
            if (!coords.TryGetValue(from, out var a) || !coords.TryGetValue(to, out var b))
                continue;

            var mid = new Point3D((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
            if (!Viewport3DProjection.TryWorldToScreen(viewport, mid, out var sc))
                continue;
            var d = (sc - mouse).Length;
            if (d <= legRadiusPx && d < bestDist)
            {
                bestDist = d;
                bestLeg = new SurveyPickLeg(shot, a, b);
            }
        }

        return bestLeg;
    }
}
