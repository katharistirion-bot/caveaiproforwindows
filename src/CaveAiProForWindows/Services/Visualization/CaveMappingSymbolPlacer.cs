using System;
using System.Collections.Generic;
using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Auto-places speleological survey station, junction, and entrance markers on the plan view
/// using <see cref="CaveMappingSymbolCatalog"/>.
/// </summary>
public static class CaveMappingSymbolPlacer
{
    private const float DefaultSymbolSpanM = 0.85f;
    private const float DedupeRadiusM = 0.45f;

    public static IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> BuildAutoPlanSymbols(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        IReadOnlyList<ShotRecord> shots)
    {
        var manual = SurveyStationGeometry.ParsePlanMapSymbols(project);
        var placed = new List<SurveyStationGeometry.PlanMapSymbol>();
        var traverseStations = BuildTraverseAdjacency(shots);
        if (traverseStations.Count == 0)
            return placed;

        var entranceCandidates = ResolveEntranceStations(project, traverseStations, coords);
        foreach (var (station, degree) in traverseStations)
        {
            if (!coords.TryGetValue(station, out var c))
                continue;
            if (IsNearExistingManual(c.X, c.Y, manual))
                continue;

            CaveMappingSymbolCatalog.SymbolKind kind;
            string? label;
            if (entranceCandidates.Contains(station))
            {
                kind = CaveMappingSymbolCatalog.SymbolKind.Entrance;
                label = station;
            }
            else if (degree >= 3)
            {
                kind = CaveMappingSymbolCatalog.SymbolKind.JunctionMarker;
                label = station;
            }
            else if (degree == 2 || degree == 1)
            {
                kind = CaveMappingSymbolCatalog.SymbolKind.StationMarker;
                label = station;
            }
            else
                continue;

            if (IsNearExistingAuto(c.X, c.Y, placed))
                continue;

            placed.Add(new SurveyStationGeometry.PlanMapSymbol(
                c.X,
                c.Y,
                c.Z,
                label,
                Scale: 1f,
                RotationDegrees: 0f,
                IconKey: CaveMappingSymbolCatalog.IconKey(kind),
                SymbolId: CaveMappingSymbolCatalog.IconKey(kind),
                ScaleSurveyMetres: DefaultSymbolSpanM));
        }

        return placed;
    }

    private static Dictionary<string, int> BuildTraverseAdjacency(IReadOnlyList<ShotRecord> shots)
    {
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        void Touch(string? st)
        {
            if (string.IsNullOrEmpty(st))
                return;
            degree.TryGetValue(st, out var d);
            degree[st] = d + 1;
        }

        foreach (var shot in shots.Where(s => s.IsTraverseLeg))
        {
            Touch(shot.FromStation);
            Touch(shot.ToStation);
        }

        return degree;
    }

    private static HashSet<string> ResolveEntranceStations(
        CaveProjectDocument project,
        Dictionary<string, int> traverseStations,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords)
    {
        var entrances = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = traverseStations.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
        if (endpoints.Count == 0)
            return entrances;

        if (project.Lat is { } lat && project.Lon is { } lon &&
            lat is > -90 and < 90 && lon is > -180 and < 180)
        {
            var first = endpoints
                .OrderBy(st => coords.TryGetValue(st, out var c) ? c.X * c.X + c.Y * c.Y : float.MaxValue)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(first))
                entrances.Add(first);
            return entrances;
        }

        foreach (var st in endpoints)
            entrances.Add(st);
        return entrances;
    }

    private static bool IsNearExistingManual(float x, float y, IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> manual)
    {
        var r2 = DedupeRadiusM * DedupeRadiusM;
        foreach (var sym in manual)
        {
            var dx = sym.X - x;
            var dy = sym.Y - y;
            if (dx * dx + dy * dy <= r2)
                return true;
        }

        return false;
    }

    private static bool IsNearExistingAuto(float x, float y, IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> auto)
    {
        var r2 = DedupeRadiusM * DedupeRadiusM;
        foreach (var sym in auto)
        {
            var dx = sym.X - x;
            var dy = sym.Y - y;
            if (dx * dx + dy * dy <= r2)
                return true;
        }

        return false;
    }
}
