using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Reference catalog pins on the PLAN tab — geo → survey metres → canvas (same origin logic as X-RAY).</summary>
public static class ReferenceCatalogPlanPins
{
    public sealed class PlanReferencePin
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public double Lat { get; init; }
        public double Lon { get; init; }
        public double? DepthM { get; init; }
        public bool IsLinkedReference { get; init; }
        public bool Rich { get; init; }
    }

    public static (double Lat, double Lon)? TryResolveGeoOrigin(CaveProjectDocument project)
    {
        if (project.Lat is { } la && project.Lon is { } lo && la is >= -90 and <= 90 && lo is >= -180 and <= 180)
            return (la, lo);

        if (ReferenceSurveyLinkService.TryGetLink(project, out var link) && link != null)
            return (link.Lat, link.Lon);

        var gps = SurveyStationGpsCatalog.Build(project);
        if (gps.Count > 0)
        {
            var first = gps.Values.First();
            return (first.Lat, first.Lon);
        }

        return null;
    }

    /// <summary>Convert WGS84 to survey local metres (X east, Y north) using entrance as origin.</summary>
    public static (float X, float Y)? TryGeoToSurveyMetres(
        double pinLat,
        double pinLon,
        double originLat,
        double originLon)
    {
        if (pinLat is < -90 or > 90 || pinLon is < -180 or > 180)
            return null;

        var latPerMetre = 1.0 / XRayProjection.MetresPerDegreeLatitude;
        var lonPerMetre = 1.0 / (XRayProjection.MetresPerDegreeLatitude *
                                 Math.Max(1e-6, Math.Cos(originLat * Math.PI / 180.0)));
        var y = (pinLat - originLat) / latPerMetre;
        var x = (pinLon - originLon) / lonPerMetre;
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return null;
        return ((float)x, (float)y);
    }

    public static Point? TryPinToCanvas(
        PlanReferencePin pin,
        CaveProjectDocument project,
        PlanCanvasSurveyLayout layout)
    {
        var origin = TryResolveGeoOrigin(project);
        if (origin == null)
            return null;

        var (originLat, originLon) = origin.Value;
        var metres = TryGeoToSurveyMetres(pin.Lat, pin.Lon, originLat, originLon);
        if (metres == null)
            return null;

        return layout.WorldToCanvas(metres.Value.X, metres.Value.Y);
    }

    public static IReadOnlyList<PlanReferencePin> CollectPins(CaveProjectDocument project)
    {
        var list = new List<PlanReferencePin>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ReferenceSurveyLinkService.TryGetLink(project, out var link) && link != null)
        {
            list.Add(new PlanReferencePin
            {
                Id = link.Id,
                Name = link.Name,
                Lat = link.Lat,
                Lon = link.Lon,
                DepthM = null,
                IsLinkedReference = true,
                Rich = true,
            });
            seen.Add(link.Id);
        }

        var bounds = ReferenceCatalogNearbyPins.TryComputeSurveyBounds(project);
        if (bounds == null)
            return list;

        var index = ReferenceCatalogFetchService.TryLoadCachedIndexEntries();
        if (index.Count == 0)
            return list;

        var (minLat, maxLat, minLon, maxLon) = bounds.Value;
        foreach (var entry in ReferenceCatalogNearbyPins.FindInBounds(index, minLat, maxLat, minLon, maxLon))
        {
            if (!seen.Add(entry.Id))
                continue;
            list.Add(new PlanReferencePin
            {
                Id = entry.Id,
                Name = entry.Name,
                Lat = entry.Lat,
                Lon = entry.Lon,
                DepthM = entry.DepthM,
                IsLinkedReference = false,
                Rich = entry.Rich,
            });
        }

        return list;
    }
}
