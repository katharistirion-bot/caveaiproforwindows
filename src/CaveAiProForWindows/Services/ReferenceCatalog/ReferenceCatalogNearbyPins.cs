using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Finds reference index entries inside a geographic bounding box (for X-RAY / Plan overlays).</summary>
public static class ReferenceCatalogNearbyPins
{
    public const int DefaultMaxPins = 50;

    public static IReadOnlyList<ReferenceCaveIndexEntry> FindInBounds(
        IReadOnlyList<ReferenceCaveIndexEntry> index,
        double minLat,
        double maxLat,
        double minLon,
        double maxLon,
        int maxResults = DefaultMaxPins)
    {
        if (index.Count == 0)
            return [];

        return index
            .Where(e => e.HasValidCoordinate())
            .Where(e => e.Lat >= minLat && e.Lat <= maxLat && e.Lon >= minLon && e.Lon <= maxLon)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();
    }

    public static (double MinLat, double MaxLat, double MinLon, double MaxLon)? TryComputeSurveyBounds(
        CaveProjectDocument project,
        double paddingDegrees = 0.02)
    {
        if (project.Lat is double lat && project.Lon is double lon && lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
        {
            return (lat - paddingDegrees, lat + paddingDegrees, lon - paddingDegrees, lon + paddingDegrees);
        }

        var coords = SurveyStationGpsCatalog.Build(project);
        if (coords.Count == 0)
            return null;

        var lats = coords.Values.Select(v => v.Lat).ToList();
        var lons = coords.Values.Select(v => v.Lon).ToList();
        return (
            lats.Min() - paddingDegrees,
            lats.Max() + paddingDegrees,
            lons.Min() - paddingDegrees,
            lons.Max() + paddingDegrees);
    }
}
