using System.Windows.Media.Media3D;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Ordered tunnel centerline samples for fly-through animation and recording.</summary>
public static class CaveViewport3DFlyThroughPathBuilder
{
    public sealed record PathSample(Point3D Position, Vector3D Tangent);

    public static IReadOnlyList<PathSample> BuildPath(CaveProjectDocument project, float sampleM = 0.5f)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var centerline = CaveSurveyTubeMeshBuilder.BuildTraverseCenterlinePath(project.Shots, coords, sampleM);
        if (centerline.Count < 2)
            return Array.Empty<PathSample>();

        var raw = centerline
            .Select(p => (p.Position, p.Tangent))
            .ToList();
        return SmoothAndResample(raw);
    }

    public static bool TrySampleAt(
        IReadOnlyList<PathSample> path,
        double normalizedProgress,
        out Point3D position,
        out Vector3D tangent)
    {
        position = default;
        tangent = new Vector3D(0, 1, 0);
        if (path.Count < 2)
            return false;

        var total = path.Count - 1;
        var scaled = Math.Clamp(normalizedProgress, 0, 1) * total;
        var seg = (int)Math.Floor(scaled);
        if (seg >= total)
            seg = total - 1;
        var t = scaled - seg;

        var a = path[seg];
        var b = path[seg + 1];
        position = new Point3D(
            a.Position.X + (b.Position.X - a.Position.X) * t,
            a.Position.Y + (b.Position.Y - a.Position.Y) * t,
            a.Position.Z + (b.Position.Z - a.Position.Z) * t);
        tangent = a.Tangent + (b.Tangent - a.Tangent) * t;
        if (tangent.LengthSquared < 1e-12)
            tangent = b.Tangent;
        tangent.Normalize();
        return true;
    }

    /// <summary>Eye position and look-ahead direction for tunnel camera.</summary>
    public static bool TrySampleTunnelCamera(
        IReadOnlyList<PathSample> path,
        double normalizedProgress,
        double lookAheadNormalized,
        out Point3D eye,
        out Vector3D lookDirection)
    {
        eye = default;
        lookDirection = new Vector3D(0, 1, 0);
        if (!TrySampleAt(path, normalizedProgress, out eye, out _))
            return false;

        var ahead = Math.Clamp(normalizedProgress + lookAheadNormalized, 0, 1);
        if (!TrySampleAt(path, ahead, out var target, out var tangent))
            return false;

        lookDirection = target - eye;
        if (lookDirection.LengthSquared < 1e-8)
            lookDirection = tangent;
        lookDirection.Normalize();
        return true;
    }

    private static IReadOnlyList<PathSample> SmoothAndResample(
        IReadOnlyList<(Point3D Position, Vector3D Tangent)> raw)
    {
        var smoothed = new List<PathSample>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var tangent = SmoothTangent(raw, i);
            smoothed.Add(new PathSample(raw[i].Position, tangent));
        }

        return smoothed;
    }

    private static Vector3D SmoothTangent(
        IReadOnlyList<(Point3D Position, Vector3D Tangent)> raw,
        int index)
    {
        var i0 = Math.Max(0, index - 1);
        var i1 = index;
        var i2 = Math.Min(raw.Count - 1, index + 1);
        var t = raw[i0].Tangent + raw[i1].Tangent * 2 + raw[i2].Tangent;
        if (t.LengthSquared < 1e-12)
            t = raw[i1].Tangent;
        t.Normalize();
        return t;
    }
}
