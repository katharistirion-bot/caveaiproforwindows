using System.Globalization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyLoopQc;

namespace CaveAiProForWindows.Services.SurveyAnalysis;

public enum LoopAdjustmentMethod
{
    /// <summary>Distributes misclosure proportional to leg length (Bowditch / compass rule).</summary>
    CompassRule,

    /// <summary>Iterative weighted least-squares refinement on loop legs.</summary>
    WeightedLeastSquares,

    /// <summary>One weighted solve of every free station, holding fixed stations and equates.</summary>
    NetworkLeastSquares,
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
        foreach (var info in SurveyLoopQcAnalyzer.AnalyzeLoops(project))
        {
            if (TryDescribeLoop(info.StationCycle.ToList(), trav, coords, out var desc))
                loops.Add(desc);
        }

        return loops;
    }

    public static SurveyLoopAdjustmentResult Adjust(
        CaveProjectDocument project,
        LoopAdjustmentMethod method = LoopAdjustmentMethod.CompassRule)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (method == LoopAdjustmentMethod.NetworkLeastSquares)
            return AdjustNetwork(project);

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

        var misAfter = MaxLegCoordinateError(loops, project.Shots, adjusted);

        return new SurveyLoopAdjustmentResult(
            method,
            adjusted.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            loops,
            misBefore,
            misAfter);
    }

    private static SurveyLoopAdjustmentResult AdjustNetwork(CaveProjectDocument project)
    {
        var loops = DetectLoops(project);
        var solved = SurveyNetworkAdjuster.Solve(project);
        var misBefore = loops.Sum(l => l.MisclosureMeters);
        var misAfter = loops.Count == 0
            ? 0
            : MaxLegCoordinateError(loops, project.Shots, solved);
        return new SurveyLoopAdjustmentResult(
            LoopAdjustmentMethod.NetworkLeastSquares,
            solved,
            loops,
            misBefore,
            misAfter);
    }

    private readonly record struct DirectedLeg(ShotRecord Shot, string FromStation, string ToStation, bool Reversed)
    {
        public double Distance => Shot.Distance;
    }

    private static void ApplyCompassRule(
        SurveyLoopDescriptor loop,
        Dictionary<string, (float X, float Y, float Z)> coords,
        CaveProjectDocument project)
    {
        var cycle = RotateCycleToHeld(loop.Stations, EntranceStation(project));
        var legs = CollectLoopLegs(cycle, project.Shots);
        if (legs.Count == 0)
            return;

        var totalLen = legs.Sum(l => l.Distance);
        if (totalLen < 1e-6)
            return;

        double misX = 0, misY = 0, misZ = 0;
        foreach (var leg in legs)
        {
            var delta = DirectedDelta(leg);
            misX += delta.dx;
            misY += delta.dy;
            misZ += delta.dz;
        }

        var held = cycle[0];
        var corrections = new Dictionary<string, (double dx, double dy, double dz)>(StringComparer.OrdinalIgnoreCase);
        var cumX = 0.0;
        var cumY = 0.0;
        var cumZ = 0.0;

        foreach (var leg in legs)
        {
            var w = leg.Distance / totalLen;
            cumX += -misX * w;
            cumY += -misY * w;
            cumZ += -misZ * w;
            if (leg.ToStation.Equals(held, StringComparison.OrdinalIgnoreCase))
                continue;
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
        ApplyCompassRule(loop, coords, project);

        var cycle = RotateCycleToHeld(loop.Stations, EntranceStation(project));
        var legs = CollectLoopLegs(cycle, project.Shots);
        var totalLen = legs.Sum(l => l.Distance);
        if (totalLen < 1e-6)
            return;
        var held = cycle[0];

        for (var iter = 0; iter < 3; iter++)
        {
            foreach (var leg in legs)
            {
                if (leg.ToStation.Equals(held, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!coords.TryGetValue(leg.FromStation, out var a) ||
                    !coords.TryGetValue(leg.ToStation, out var b))
                    continue;

                var obs = DirectedDelta(leg);
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

    private static double MaxLegCoordinateError(
        IReadOnlyList<SurveyLoopDescriptor> loops,
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, (float X, float Y, float Z)> coords)
    {
        var worst = 0.0;
        foreach (var loop in loops)
        {
            foreach (var leg in CollectLoopLegs(loop.Stations, shots))
            {
                if (!coords.TryGetValue(leg.FromStation, out var a) ||
                    !coords.TryGetValue(leg.ToStation, out var b))
                    return double.PositiveInfinity;
                var obs = DirectedDelta(leg);
                var err = Math.Sqrt(
                    Math.Pow((b.X - a.X) - obs.dx, 2) +
                    Math.Pow((b.Y - a.Y) - obs.dy, 2) +
                    Math.Pow((b.Z - a.Z) - obs.dz, 2));
                if (err > worst)
                    worst = err;
            }
        }

        return worst;
    }

    private static string? EntranceStation(CaveProjectDocument project) =>
        project.Shots.FirstOrDefault(s => s.IsNetworkLeg)?.FromStation;

    private static IReadOnlyList<string> RotateCycleToHeld(IReadOnlyList<string> cycle, string? held)
    {
        if (cycle.Count < 2 || string.IsNullOrWhiteSpace(held))
            return cycle;
        var body = cycle.Take(cycle.Count - 1).ToList();
        var idx = body.FindIndex(s => s.Equals(held, StringComparison.OrdinalIgnoreCase));
        if (idx <= 0)
            return cycle;
        var rotated = body.Skip(idx).Concat(body.Take(idx)).ToList();
        rotated.Add(rotated[0]);
        return rotated;
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
            if (!coords.ContainsKey(leg.FromStation) || !coords.ContainsKey(leg.ToStation))
                return false;
            var obs = DirectedDelta(leg);
            sx += obs.dx;
            sy += obs.dy;
            sz += obs.dz;
            totalLen += leg.Distance;
        }

        var mis = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        var closing = $"{legs[^1].FromStation}→{legs[^1].ToStation}";
        desc = new SurveyLoopDescriptor(
            cycle.ToList(),
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

    private static List<DirectedLeg> CollectLoopLegs(IReadOnlyList<string> cycle, IReadOnlyList<ShotRecord> trav)
    {
        var legs = new List<DirectedLeg>();
        for (var i = 0; i < cycle.Count - 1; i++)
        {
            var a = cycle[i];
            var b = cycle[i + 1];
            var forward = trav.FirstOrDefault(s =>
                s.FromStation.Equals(a, StringComparison.OrdinalIgnoreCase) &&
                s.ToStation.Equals(b, StringComparison.OrdinalIgnoreCase));
            if (forward != null)
            {
                legs.Add(new DirectedLeg(forward, a, b, false));
                continue;
            }

            var reverse = trav.FirstOrDefault(s =>
                s.FromStation.Equals(b, StringComparison.OrdinalIgnoreCase) &&
                s.ToStation.Equals(a, StringComparison.OrdinalIgnoreCase));
            if (reverse == null)
                return new List<DirectedLeg>();
            legs.Add(new DirectedLeg(reverse, a, b, true));
        }

        return legs;
    }

    private static (double dx, double dy, double dz) DirectedDelta(DirectedLeg leg)
    {
        var (dx, dy, dz) = SurveyLegGeometry.Delta(leg.Shot);
        return leg.Reversed ? (-dx, -dy, -dz) : (dx, dy, dz);
    }

    /// <summary>
    /// Writes adjusted coordinates into <see cref="CaveProjectDocument.PlanStationPositionOverrides"/>
    /// without modifying raw shot measurements (non-destructive office preview / apply-to-copy).
    /// Compares against traverse-only coords so existing overrides on stations the loop did not
    /// move are never wiped (re-apply / Survey Cloud save).
    /// </summary>
    public static void ApplyToPlanOverrides(
        CaveProjectDocument project,
        SurveyLoopAdjustmentResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(result);

        var traverseOnly = SurveyStationGeometry.CalculatePlanCoordinates(
            project.Shots,
            (float)project.Alt,
            planStationPositionOverrides: null);
        foreach (var (station, adjusted) in result.AdjustedCoordinates)
        {
            if (!traverseOnly.TryGetValue(station, out var raw))
            {
                project.PlanStationPositionOverrides[station] = new PlanStationPositionOverride(
                    adjusted.X,
                    adjusted.Y,
                    adjusted.Z);
                continue;
            }

            var dx = adjusted.X - raw.X;
            var dy = adjusted.Y - raw.Y;
            var dz = adjusted.Z - raw.Z;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6 && Math.Abs(dz) < 1e-6)
                continue;

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
