using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public sealed record LoopClosingLegHighlight(
    string FromStation,
    string ToStation,
    float MidX,
    float MidY,
    double MisclosureMeters,
    LoopClosureSeverity Severity,
    string Label);

/// <summary>Detects traverse legs that close onto an existing station (loop closure).</summary>
public static class SurveyLoopClosureHighlighter
{
    public static IReadOnlyList<LoopClosingLegHighlight> Detect(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var inv = CultureInfo.InvariantCulture;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<LoopClosingLegHighlight>();

        foreach (var shot in project.Shots.Where(s => s.IsTraverseLeg))
        {
            var from = shot.FromStation.Trim();
            var to = shot.ToStation.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                continue;

            if (visited.Contains(to) &&
                coords.TryGetValue(from, out var a) &&
                coords.TryGetValue(to, out var b))
            {
                var az = shot.Azimuth * (Math.PI / 180.0);
                var cl = shot.Clino * (Math.PI / 180.0);
                var h = shot.Distance * Math.Cos(cl);
                var dz = shot.Distance * Math.Sin(cl);
                var ex = a.X + (float)(h * Math.Sin(az));
                var ey = a.Y + (float)(h * Math.Cos(az));
                var ez = a.Z + (float)dz;
                var dx = ex - b.X;
                var dy = ey - b.Y;
                var dzErr = ez - b.Z;
                var mis = Math.Sqrt(dx * dx + dy * dy + dzErr * dzErr);
                var severity = LoopClosureSeverityClassifier.Classify(mis);
                var midX = (a.X + b.X) * 0.5f;
                var midY = (a.Y + b.Y) * 0.5f;
                var errLabel = LoopClosureSeverityClassifier.FormatMisclosureLabel(mis);
                list.Add(new LoopClosingLegHighlight(
                    from,
                    to,
                    midX,
                    midY,
                    mis,
                    severity,
                    $"loop {errLabel} ({LoopClosureSeverityClassifier.SeverityCaption(severity)})"));
            }

            visited.Add(from);
            visited.Add(to);
        }

        return list;
    }
}
