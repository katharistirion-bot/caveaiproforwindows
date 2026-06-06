using System.Globalization;
using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Quick global metrics for PLAN / Sketch HUD (same reductions as traverse reduction).</summary>
public static class SurveyPlanHudStats
{
    /// <returns>Station count, total traverse tape metres, vertical Z span metres (stations), one-line summary.</returns>
    public static (int Stations, double TotalTapeM, double ZSpanM, string Line) Compute(CaveProjectDocument? project)
    {
        if (project == null)
            return (0, 0, 0, "No project.");

        var inv = CultureInfo.InvariantCulture;
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var trav = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        var sumTape = trav.Sum(s => (double)s.Distance);

        double zSpan = 0;
        if (coords.Count > 1)
            zSpan = coords.Values.Max(c => c.Z) - coords.Values.Min(c => c.Z);

        var stationCount = coords.Count;
        if (stationCount == 0 && trav.Count > 0)
        {
            var names = trav.SelectMany(t => new[] { t.FromStation.Trim(), t.ToStation.Trim() })
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            stationCount = names.Count();
        }

        var nm = string.IsNullOrWhiteSpace(project.Name) ? "Project" : project.Name.Trim();
        var st = trav.Count == 0 && stationCount == 0
            ? $"{nm}: no traverse shots yet"
            : $"{stationCount} station(s)  ·  {sumTape.ToString("0.##", inv)} m traverse  ·  Z span {zSpan.ToString("0.##", inv)} m";

        return (stationCount, sumTape, zSpan, st);
    }
}
