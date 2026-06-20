using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

public sealed record SimilarCaveMatch(ReferenceCaveIndexEntry Entry, double DistanceKm, double Score);

public static class ReferenceCatalogSimilarCaves
{
    public const double DefaultRadiusKm = 25;
    public const int MaxResults = 8;

    public static IReadOnlyList<SimilarCaveMatch> FindSimilar(
        ReferenceCaveIndexEntry source,
        IReadOnlyList<ReferenceCaveIndexEntry> candidates,
        double radiusKm = DefaultRadiusKm,
        int maxResults = MaxResults)
    {
        if (!source.HasValidCoordinate())
            return [];

        var ranked = new List<SimilarCaveMatch>();
        foreach (var entry in candidates)
        {
            if (string.Equals(entry.Id, source.Id, StringComparison.Ordinal) || !entry.HasValidCoordinate())
                continue;

            var distKm = GeoHaversine.DistanceKm(source.Lat, source.Lon, entry.Lat, entry.Lon);
            if (distKm > radiusKm)
                continue;

            var score = 100.0 - distKm;
            if (!string.IsNullOrWhiteSpace(source.Country) &&
                string.Equals(entry.Country, source.Country, StringComparison.OrdinalIgnoreCase))
                score += 3;
            if (!string.IsNullOrWhiteSpace(source.Region) &&
                string.Equals(entry.Region, source.Region, StringComparison.OrdinalIgnoreCase))
                score += 2;
            var sourceBand = DepthBand(source.DepthM);
            if (sourceBand != "unknown" && DepthBand(entry.DepthM) == sourceBand)
                score += 2;

            ranked.Add(new SimilarCaveMatch(entry, distKm, score));
        }

        return ranked
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.DistanceKm)
            .Take(maxResults)
            .ToList();
    }

    private static string DepthBand(double? depthM)
    {
        if (depthM is not > 0)
            return "unknown";
        if (depthM < 50)
            return "shallow";
        if (depthM < 200)
            return "medium";
        return "deep";
    }
}