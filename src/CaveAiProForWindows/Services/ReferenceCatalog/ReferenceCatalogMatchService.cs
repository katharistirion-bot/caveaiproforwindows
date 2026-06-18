using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

public sealed record ReferenceCatalogMatch(
    ReferenceCaveIndexEntry Entry,
    double Score,
    double DistanceKm);

/// <summary>Suggests reference catalog matches for a survey project (name + haversine &lt; 5 km).</summary>
public static class ReferenceCatalogMatchService
{
    private static readonly Regex NormalizeName = new(@"[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const double MaxDistanceKm = 5.0;

    public static IReadOnlyList<ReferenceCatalogMatch> FindMatches(
        CaveProjectDocument project,
        IReadOnlyList<ReferenceCaveIndexEntry> index,
        int maxResults = 5)
    {
        if (!project.Lat.HasValue || !project.Lon.HasValue)
            return [];

        var lat = project.Lat.Value;
        var lon = project.Lon.Value;
        var normProject = Normalize(project.Name);

        var candidates = index
            .Where(e => e.HasValidCoordinate())
            .Select(e =>
            {
                var dist = GeoHaversine.DistanceKm(lat, lon, e.Lat, e.Lon);
                var nameScore = NameSimilarity(normProject, Normalize(e.Name));
                var score = nameScore * 0.6 + Math.Max(0, 1 - dist / MaxDistanceKm) * 0.4;
                return new ReferenceCatalogMatch(e, score, dist);
            })
            .Where(m => m.DistanceKm <= MaxDistanceKm && m.Score >= 0.35)
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.DistanceKm)
            .Take(maxResults)
            .ToList();

        return candidates;
    }

    public static string FormatMatchSummary(ReferenceCaveIndexEntry entry, double distanceKm)
    {
        var parts = new List<string> { entry.Name };
        if (!string.IsNullOrWhiteSpace(entry.Country))
            parts.Add(entry.Country);
        parts.Add($"{distanceKm:0.#} km");
        if (entry.DepthM is > 0)
            parts.Add($"depth {entry.DepthM:0.#} m");
        return string.Join(" · ", parts);
    }

    private static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        var lower = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var ch in lower)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return NormalizeName.Replace(sb.ToString(), "");
    }

    private static double NameSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return 1;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            return 0.85;
        return 0;
    }
}
