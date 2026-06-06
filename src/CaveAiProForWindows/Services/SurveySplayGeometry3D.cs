using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>LRUD / tape splays as 3D line segments (survey X, Y, Z metres).</summary>
public static class SurveySplayGeometry3D
{
    private const float Eps = 1e-4f;

    public static IReadOnlyList<(Point3D A, Point3D B)> BuildSegments(
        IReadOnlyList<ShotRecord> shots,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var list = new List<(Point3D, Point3D)>();
        foreach (var shot in shots)
        {
            if (shot.IsTraverseLeg)
                continue;
            if (!coords.TryGetValue(shot.FromStation, out var from))
                continue;

            var origin = new Point3D(from.X, from.Y, from.Z);
            var azRad = shot.Azimuth * (Math.PI / 180.0);
            var clRad = shot.Clino * (Math.PI / 180.0);
            var sinA = Math.Sin(azRad);
            var cosA = Math.Cos(azRad);
            var sinCl = Math.Sin(clRad);
            var cosCl = Math.Cos(clRad);

            var fhx = sinA * cosCl;
            var fhy = cosA * cosCl;
            var fhz = sinCl;
            var lx = -cosA;
            var ly = sinA;
            var rx = cosA;
            var ry = -sinA;

            var (sl, sr, su, sd) = shot.EffectivePlanLrud();
            var hasDims = sl > Eps || sr > Eps || su > Eps || sd > Eps;
            if (hasDims)
            {
                if (sl > Eps)
                    list.Add((origin, Offset(origin, lx * sl, ly * sl, 0)));
                if (sr > Eps)
                    list.Add((origin, Offset(origin, rx * sr, ry * sr, 0)));
                if (su > Eps)
                    list.Add((origin, Offset(origin, fhx * su, fhy * su, Math.Abs(fhz) > Eps ? fhz * su : su)));
                if (sd > Eps)
                    list.Add((origin, Offset(origin, -fhx * sd, -fhy * sd, Math.Abs(fhz) > Eps ? -fhz * sd : -sd)));
            }

            if (shot.Distance > Eps && !hasDims)
            {
                var d = shot.Distance;
                list.Add((origin, Offset(origin, fhx * d, fhy * d, fhz * d)));
            }
        }

        return list;
    }

    private static Point3D Offset(Point3D p, double dx, double dy, double dz) =>
        new(p.X + dx, p.Y + dy, p.Z + dz);
}
