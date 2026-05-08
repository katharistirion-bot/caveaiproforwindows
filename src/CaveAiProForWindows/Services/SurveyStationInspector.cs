using System.Linq;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Resolves recorded <see cref="ShotRecord"/> rows for inspecting a traverse station on the map.</summary>
public static class SurveyStationInspector
{
    public static ShotRecord? TryGetRepresentativeShotForStation(CaveProjectDocument? project, string? stationName)
    {
        if (project?.Shots is not { Count: > 0 } shots)
            return null;
        var name = (stationName ?? "").Trim();
        if (name.Length == 0)
            return null;

        static bool Eq(string? a, string needle) =>
            string.Equals((a ?? "").Trim(), needle, StringComparison.OrdinalIgnoreCase);

        return shots.FirstOrDefault(s => s.IsTraverseLeg && Eq(s.FromStation, name))
               ?? shots.FirstOrDefault(s => s.IsTraverseLeg && Eq(s.ToStation, name))
               ?? shots.FirstOrDefault(s => Eq(s.FromStation, name))
               ?? shots.FirstOrDefault(s => !ShotRecord.IsSplayDestination(s.ToStation) && Eq(s.ToStation, name));
    }
}
