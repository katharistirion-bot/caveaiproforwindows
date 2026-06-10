using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Compares two survey projects at leg and station level.</summary>
public static class SurveyCompareService
{
    public sealed record LegKey(string From, string To);

    public sealed record LegDiffRow(
        string ChangeKind,
        string FromStation,
        string ToStation,
        string DetailA,
        string DetailB);

    public sealed record StationDiffRow(
        string ChangeKind,
        string Station,
        string DetailA,
        string DetailB);

    public sealed record CompareResult(
        IReadOnlyList<LegDiffRow> LegChanges,
        IReadOnlyList<StationDiffRow> StationChanges,
        int LegsAdded,
        int LegsRemoved,
        int LegsChanged,
        int StationsAdded,
        int StationsRemoved,
        int StationsMoved);

    public static CompareResult Compare(CaveProjectDocument? a, CaveProjectDocument? b)
    {
        var legsA = IndexLegs(a);
        var legsB = IndexLegs(b);
        var legRows = new List<LegDiffRow>();

        foreach (var key in legsA.Keys.Union(legsB.Keys, LegKeyComparer.Instance))
        {
            legsA.TryGetValue(key, out var sa);
            legsB.TryGetValue(key, out var sb);
            if (sa == null)
            {
                legRows.Add(new LegDiffRow("Added", key.From, key.To, "—", FormatLeg(sb!)));
                continue;
            }

            if (sb == null)
            {
                legRows.Add(new LegDiffRow("Removed", key.From, key.To, FormatLeg(sa), "—"));
                continue;
            }

            if (!LegsEqual(sa, sb))
                legRows.Add(new LegDiffRow("Changed", key.From, key.To, FormatLeg(sa), FormatLeg(sb)));
        }

        var coordsA = a != null ? SurveyStationGeometry.CalculatePlanCoordinates(a) : new Dictionary<string, SurveyStationGeometry.StationPlanCoords>();
        var coordsB = b != null ? SurveyStationGeometry.CalculatePlanCoordinates(b) : new Dictionary<string, SurveyStationGeometry.StationPlanCoords>();
        var stationRows = new List<StationDiffRow>();
        var allStations = coordsA.Keys.Union(coordsB.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var st in allStations.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var hasA = coordsA.TryGetValue(st, out var ca);
            var hasB = coordsB.TryGetValue(st, out var cb);
            if (!hasA)
            {
                stationRows.Add(new StationDiffRow("Added", st, "—", FormatCoord(cb!)));
                continue;
            }

            if (!hasB)
            {
                stationRows.Add(new StationDiffRow("Removed", st, FormatCoord(ca!), "—"));
                continue;
            }

            if (!CoordsNear(ca!, cb!))
                stationRows.Add(new StationDiffRow("Moved", st, FormatCoord(ca!), FormatCoord(cb!)));
        }

        return new CompareResult(
            legRows,
            stationRows,
            legRows.Count(r => r.ChangeKind == "Added"),
            legRows.Count(r => r.ChangeKind == "Removed"),
            legRows.Count(r => r.ChangeKind == "Changed"),
            stationRows.Count(r => r.ChangeKind == "Added"),
            stationRows.Count(r => r.ChangeKind == "Removed"),
            stationRows.Count(r => r.ChangeKind == "Moved"));
    }

    private static Dictionary<LegKey, ShotRecord> IndexLegs(CaveProjectDocument? project)
    {
        var map = new Dictionary<LegKey, ShotRecord>(LegKeyComparer.Instance);
        if (project?.Shots == null)
            return map;
        foreach (var s in project.Shots.Where(static x => x.IsTraverseLeg))
        {
            var key = new LegKey((s.FromStation ?? "").Trim(), (s.ToStation ?? "").Trim());
            if (key.From.Length == 0 || key.To.Length == 0)
                continue;
            map[key] = s;
        }

        return map;
    }

    private static bool LegsEqual(ShotRecord a, ShotRecord b) =>
        Math.Abs(a.Distance - b.Distance) < 0.001 &&
        Math.Abs(a.Azimuth - b.Azimuth) < 0.01 &&
        Math.Abs(a.Clino - b.Clino) < 0.01;

    private static string FormatLeg(ShotRecord s)
    {
        var inv = CultureInfo.InvariantCulture;
        return $"{s.Distance.ToString("0.###", inv)} m · Az {s.Azimuth.ToString("0.#", inv)}° · Cl {s.Clino.ToString("0.#", inv)}°";
    }

    private static string FormatCoord(SurveyStationGeometry.StationPlanCoords c)
    {
        var inv = CultureInfo.InvariantCulture;
        return $"({c.X.ToString("0.##", inv)}, {c.Y.ToString("0.##", inv)}, {c.Z.ToString("0.##", inv)})";
    }

    private static bool CoordsNear(SurveyStationGeometry.StationPlanCoords a, SurveyStationGeometry.StationPlanCoords b) =>
        Math.Abs(a.X - b.X) < 0.05f &&
        Math.Abs(a.Y - b.Y) < 0.05f &&
        Math.Abs(a.Z - b.Z) < 0.05f;

    private sealed class LegKeyComparer : IEqualityComparer<LegKey>
    {
        public static readonly LegKeyComparer Instance = new();

        public bool Equals(LegKey? x, LegKey? y) =>
            x != null && y != null &&
            string.Equals(x.From, y.From, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.To, y.To, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(LegKey obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.From),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.To));
    }
}
