using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Associates plan annotations with the nearest traverse station.</summary>
public static class AndroidSurveyStationMatcher
{
    public static string? FindNearestStation(
        float surveyX,
        float surveyY,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        float maxDistanceMetres = 12f)
    {
        if (coords.Count == 0)
            return null;

        string? best = null;
        var bestD2 = double.MaxValue;
        foreach (var kv in coords)
        {
            var dx = kv.Value.X - surveyX;
            var dy = kv.Value.Y - surveyY;
            var d2 = (double)dx * dx + (double)dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = kv.Key;
            }
        }

        if (best == null)
            return null;

        var maxD2 = (double)maxDistanceMetres * maxDistanceMetres;
        return bestD2 <= maxD2 ? best : null;
    }
}
