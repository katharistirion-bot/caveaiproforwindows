using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// LRUD / radial splay shots (non-traverse legs) as 2D plan segments from <see cref="SurveyStationGeometry.CalculatePlanCoordinates"/> frame.
/// </summary>
public static class SurveySplayGeometry
{
    private const float Eps = 1e-4f;

    /// <summary>Each entry is a line segment in survey plan metres (x,y).</summary>
    public static IReadOnlyList<(float x1, float y1, float x2, float y2)> BuildPlanSplaySegments(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var list = new List<(float, float, float, float)>();
        foreach (var shot in shots)
        {
            if (shot.IsTraverseLeg)
                continue;
            if (!coords.TryGetValue(shot.FromStation, out var from))
                continue;

            var fx = from.X;
            var fy = from.Y;
            var azRad = shot.Azimuth * (Math.PI / 180.0);
            var clRad = shot.Clino * (Math.PI / 180.0);
            var sinA = (float)Math.Sin(azRad);
            var cosA = (float)Math.Cos(azRad);
            var cosCl = (float)Math.Cos(clRad);

            // Horizontal plan unit matching traverse reduction (same as SurveyStationGeometry).
            var fhx = sinA * cosCl;
            var fhy = cosA * cosCl;
            // Left / right perpendicular in plan (CCW from forward).
            var lx = -cosA;
            var ly = sinA;
            var rx = cosA;
            var ry = -sinA;

            var (sl, sr, su, sd) = shot.EffectivePlanLrud();
            var hasDims = sl > Eps || sr > Eps || su > Eps || sd > Eps;
            if (hasDims)
            {
                if (sl > Eps)
                    list.Add((fx, fy, fx + lx * sl, fy + ly * sl));
                if (sr > Eps)
                    list.Add((fx, fy, fx + rx * sr, fy + ry * sr));
                if (su > Eps)
                    list.Add((fx, fy, fx + fhx * su, fy + fhy * su));
                if (sd > Eps)
                    list.Add((fx, fy, fx - fhx * sd, fy - fhy * sd));
            }

            if (shot.Distance > Eps && !hasDims)
            {
                var h = shot.Distance * cosCl;
                var dx = h * sinA;
                var dy = h * cosA;
                list.Add((fx, fy, fx + dx, fy + dy));
            }
        }

        return list;
    }
}
