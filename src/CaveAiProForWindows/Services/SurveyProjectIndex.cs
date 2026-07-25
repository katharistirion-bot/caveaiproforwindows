using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Indexes survey projects for compare UIs. Prefers stable LinkedLibraryCaveId,
/// and disambiguates duplicate names instead of silently dropping them via GroupBy.First().
/// </summary>
public static class SurveyProjectIndex
{
    public static Dictionary<string, CaveProjectDocument> Build(
        IEnumerable<CaveProjectDocument> list,
        out int duplicateNameGroups)
    {
        var projects = list?.Where(p => p != null).ToList() ?? new List<CaveProjectDocument>();
        var nameCounts = projects
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        duplicateNameGroups = nameCounts.Count(kv => kv.Value > 1);

        var map = new Dictionary<string, CaveProjectDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var key = DisplayKey(p, nameCounts);
            var unique = key;
            var n = 2;
            while (map.ContainsKey(unique))
            {
                unique = $"{key} #{n}";
                n++;
            }
            map[unique] = p;
        }

        return map;
    }

    public static string DisplayKey(CaveProjectDocument p, IReadOnlyDictionary<string, int> nameCounts)
    {
        var linked = p.LinkedLibraryCaveId?.Trim();
        if (!string.IsNullOrEmpty(linked))
            return $"id:{linked}";

        var name = string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name.Trim();
        if (nameCounts.TryGetValue(name, out var count) && count > 1)
        {
            var shots = p.Shots?.Count ?? 0;
            var lat = p.Lat?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?";
            var lon = p.Lon?.ToString("0.###", CultureInfo.InvariantCulture) ?? "?";
            return $"{name} · {shots} shots · {lat},{lon}";
        }

        return name;
    }
}