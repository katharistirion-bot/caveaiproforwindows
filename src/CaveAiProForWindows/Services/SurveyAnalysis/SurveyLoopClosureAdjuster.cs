using System.Globalization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SurveyAnalysis;

public enum LoopAdjustmentMethod
{
    /// <summary>Distributes misclosure proportional to leg length (Bowditch / compass rule).</summary>
    CompassRule,

    /// <summary>Iterative weighted least-squares refinement on loop legs.</summary>
    WeightedLeastSquares,
}

public sealed record SurveyLoopDescriptor(
    IReadOnlyList<string> Stations,
    string ClosingLeg,
    double MisclosureMeters,
    double TotalLegLength,
    double MeanLegLength,
    int StationCount,
    double MisclosureX,
    double MisclosureY,
    double MisclosureZ);

public sealed record SurveyLoopAdjustmentResult(
    LoopAdjustmentMethod Method,
    IReadOnlyDictionary<string, (float X, float Y, float Z)> AdjustedCoordinates,
    IReadOnlyList<SurveyLoopDescriptor> Loops,
    double TotalMisclosureBefore,
    double TotalMisclosureAfter);

/// <summary>
/// Detects closed traverse loops and distributes measurement error locally (Bowditch / weighted LS).
/// Original implementation — not tied to any third-party adjustment package.
/// </summary>
public static class SurveyLoopClosureAdjuster
{
    public static IReadOnlyList<SurveyLoopDescriptor> DetectLoops(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var trav = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var loops = new List<SurveyLoopDescriptor>();

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var shot in trav)
        {
            var from = shot.FromStation.Trim();
            var to = shot.ToStation.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                continue;

            if (visited.Contains(to))
            {
                var idx = path.FindIndex(s => s.Equals(to, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    var cycle = path.Skip(idx).Append(to).ToList();
                    if (TryDescribeLoop(cycle, trav, coords, out var desc))
                        loops.Add(desc);
                }
            }
            else
            {
                if (path.Count == 0 || !path[^1].Equals(from, StringComparison.OrdinalIgnoreCase))
                    path.Add(from);
                path.Add(to);
            }

            visited.Add(from);
            visited.Add(to);
        }

        return loops;
    }

    public static SurveyLoopAdjustmentResult Adjust(
        CaveProjectDocument project,
        LoopAdjustmentMethod method = LoopAdjustmentMethod.CompassRule)
    {
        ArgumentNullException.ThrowIfNull(project);
        var baseCoords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var loops = DetectLoops(project);
        if (loops.Count == 0)
        {
            return new SurveyLoopAdjustmentResult(
                method,
                ToTupleDict(baseCoords),
                loops,
                0,
                0);
        }

        var adjusted = baseCoords.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.X, kv.Value.Y, kv.Value.Z),
            StringComparer.OrdinalIgnoreCase);

        var misBefore = loops.Sum(l => l.MisclosureMeters);

        foreach (var loop in loops.OrderByDescending(l => l.MisclosureMeters))
        {
            if (method == LoopAdjustmentMethod.WeightedLeastSquares)
                ApplyWeightedLeastSquares(loop, adjusted, project);
            else
                ApplyCompassRule(loop, adjusted, project);
        }

        var misAfter = DetectLoopsFromCoords(project, adjusted).Sum(l => l.MisclosureMeters);

        return new SurveyLoopAdjustmentResult(
            method,
            adjusted.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            loops,
            misBefore,
            misAfter);
    }

    private static void ApplyCompassRule(
        SurveyLoopDescriptor loop,
        Dictionary<string, (float X, float Y, float Z)> coords,
        CaveProjectDocument project)
    {
        var legs = CollectLoopLegs(loop.Stations, project.Shots);
        if (legs.Count == 0)
            return;

        var totalLen = legs.Sum(l => l.Distance);
        if (totalLen < 1e-6)
            return;

        var corrections = new Dictionary<string, (double dx, double dy, double dz)>(StringComparer.OrdinalIgnoreCase);
        var cumX = 0.0;
        var cumY = 0.0;
        var cumZ = 0.0;

        foreach (var leg in legs)
        {
            var w = leg.Distance / totalLen;
            var cx = -loop.MisclosureX * w;
            var cy = -loop.MisclosureY * w;
            var cz = -loop.MisclosureZ * w;
            cumX += cx;
            cumY += cy;
            cumZ += cz;

            if (!corrections.ContainsKey(leg.ToStation))
                corrections[leg.ToStation] = (cumX, cumY, cumZ);
        }

        foreach (var (station, (dx, dy, dz)) in corrections)
        {
            if (!coords.TryGetValue(station, out var c))
                continue;
            coords[station] = ((float)(c.X + dx), (float)(c.Y + dy), (float)(c.Z + dz));
        }
    }

    private static void ApplyWeightedLeastSquares(
        SurveyLoopDescriptor loop,
        Dictionary<string, (float X, float Y, float Z)> coords,
        CaveProjectDocument project)
    {
        // Two-pass: compass rule seed + single Gauss-Seidel iteration weighted by leg length.
        ApplyCompassRule(loop, coords, project);

        var legs = CollectLoopLegs(loop.Stations, project.Shots);
        var totalLen = legs.Sum(l => l.Distance);
        if (totalLen < 1e-6)
            return;

        for (var iter = 0; iter < 3; iter++)
        {
            foreach (var leg in legs)
            {
                if (!coords.TryGetValue(leg.FromStation, out var a) ||
                    !coords.TryGetValue(leg.ToStation, out var b))
                    continue;

                var obs = LegVector(leg, a);
                var errX = (b.X - a.X) - obs.dx;
                var errY = (b.Y - a.Y) - obs.dy;
                var errZ = (b.Z - a.Z) - obs.dz;
                var w = leg.Distance / totalLen * 0.5;

                coords[leg.ToStation] = (
                    (float)(b.X - errX * w),
                    (float)(b.Y - errY * w),
                    (float)(b.Z - errZ * w));
            }
        }
    }

    private static IReadOnlyList<SurveyLoopDescriptor> DetectLoopsFromCoords(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, (float X, float Y, float Z)> coords)
    {
        var trav = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var loops = new List<SurveyLoopDescriptor>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        foreach (var shot in trav)
        {
            var from = shot.FromStation.Trim();
            var to = shot.ToStation.Trim();
            if (visited.Contains(to))
            {
                var idx = path.FindIndex(s => s.Equals(to, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    var cycle = path.Skip(idx).Append(to).ToList();
                    if (TryDescribeLoopFromCoords(cycle, trav, coords, out var desc))
                        loops.Add(desc);
                }
            }
            else
            {
                if (path.Count == 0 || !path[^1].Equals(from, StringComparison.OrdinalIgnoreCase))
                    path.Add(from);
                path.Add(to);
            }

            visited.Add(from);
            visited.Add(to);
        }

        return loops;
    }

    private static bool TryDescribeLoop(
        IReadOnlyList<string> cycle,
        IReadOnlyList<ShotRecord> trav,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        out SurveyLoopDescriptor desc)
    {
        var tupleCoords = coords.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.X, kv.Value.Y, kv.Value.Z),
            StringComparer.OrdinalIgnoreCase);
        return TryDescribeLoopFromCoords(cycle, trav, tupleCoords, out desc);
    }

    private static bool TryDescribeLoopFromCoords(
        IReadOnlyList<string> cycle,
        IReadOnlyList<ShotRecord> trav,
        IReadOnlyDictionary<string, (float X, float Y, float Z)> coords,
        out SurveyLoopDescriptor desc)
    {
        desc = null!;
        if (cycle.Count < 3)
            return false;

        var legs = CollectLoopLegs(cycle, trav);
        if (legs.Count == 0)
            return false;

        double sx = 0, sy = 0, sz = 0;
        var totalLen = 0.0;
        foreach (var leg in legs)
        {
            if (!coords.TryGetValue(leg.FromStation, out var a))
                return false;
            var obs = LegVector(leg, a);
            sx += obs.dx;
            sy += obs.dy;
            sz += obs.dz;
            totalLen += leg.Distance;
        }

        var mis = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        var closing = $"{legs[^1].FromStation}→{legs[^1].ToStation}";
        desc = new SurveyLoopDescriptor(
            cycle.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            closing,
            mis,
            totalLen,
            totalLen / Math.Max(1, legs.Count),
            cycle.Count,
            sx,
            sy,
            sz);
        return true;
    }

    private static List<ShotRecord> CollectLoopLegs(IReadOnlyList<string> cycle, IReadOnlyList<ShotRecord> trav)
    {
        var legs = new List<ShotRecord>();
        for (var i = 0; i < cycle.Count - 1; i++)
        {
            var a = cycle[i];
            var b = cycle[i + 1];
            var leg = trav.FirstOrDefault(s =>
                s.FromStation.Equals(a, StringComparison.OrdinalIgnoreCase) &&
                s.ToStation.Equals(b, StringComparison.OrdinalIgnoreCase));
            if (leg == null)
                return legs;
            legs.Add(leg);
        }

        return legs;
    }

    private static (double dx, double dy, double dz) LegVector(
        ShotRecord leg,
        (float X, float Y, float Z) from)
    {
        var az = leg.Azimuth * (Math.PI / 180.0);
        var cl = leg.Clino * (Math.PI / 180.0);
        var h = leg.Distance * Math.Cos(cl);
        return (
            h * Math.Sin(az),
            h * Math.Cos(az),
            leg.Distance * Math.Sin(cl));
    }

    /// <summary>
    /// Writes adjusted coordinates into <see cref="CaveProjectDocument.PlanStationPositionOverrides"/>
    /// without modifying raw shot measurements (non-destructive office preview / apply-to-copy).
    /// </summary>
    public static void ApplyToPlanOverrides(
        CaveProjectDocument project,
        SurveyLoopAdjustmentResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);

        var baseCoords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        foreach (var (station, adjusted) in result.AdjustedCoordinates)
        {
            if (!baseCoords.TryGetValue(station, out var raw))
                continue;

            var dx = adjusted.X - raw.X;
            var dy = adjusted.Y - raw.Y;
            var dz = adjusted.Z - raw.Z;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6 && Math.Abs(dz) < 1e-6)
            {
                project.PlanStationPositionOverrides.Remove(station);
                continue;
            }

            project.PlanStationPositionOverrides[station] = new PlanStationPositionOverride(
                adjusted.X,
                adjusted.Y,
                adjusted.Z);
        }
    }

    private static IReadOnlyDictionary<string, (float X, float Y, float Z)> ToTupleDict(
        Dictionary<string, SurveyStationGeometry.StationPlanCoords> coords) =>
        coords.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.X, kv.Value.Y, kv.Value.Z),
            StringComparer.OrdinalIgnoreCase);
}
