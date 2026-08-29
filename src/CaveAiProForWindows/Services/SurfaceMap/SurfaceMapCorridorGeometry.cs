using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.SurfaceMap;

/// <summary>
/// WGS84 traverse center-line polylines for the MapLibre surface map (mirrors Android
/// <c>buildSurfaceStationCoordsGps</c> + traverse chain center lines in <c>SurfaceMapScreen.kt</c>).
/// </summary>
public static class SurfaceMapCorridorGeometry
{
    /// <summary>WGS84 equatorial radius — same as Android surface map geodesic steps.</summary>
    private const double EarthRadiusM = 6378137.0;

    /// <summary>Preview anchor when entrance GPS is missing (Athens — keeps OSM tiles meaningful for regional surveys).</summary>
    public const double DefaultPreviewAnchorLat = 37.9838;
    public const double DefaultPreviewAnchorLon = 23.7275;

    public sealed record CorridorBuildResult(
        GeoJsonFeatureCollection? Corridor,
        bool IsProvisional);

    public sealed record LatLonPoint(double Lat, double Lon);

    public sealed class GeoJsonFeatureCollection
    {
        public string Type { get; init; } = "FeatureCollection";
        public List<GeoJsonLineFeature> Features { get; init; } = new();
    }

    public sealed class GeoJsonLineFeature
    {
        public string Type { get; init; } = "Feature";
        public GeoJsonLineGeometry Geometry { get; init; } = new();
    }

    public sealed class GeoJsonLineGeometry
    {
        public string Type { get; init; } = "LineString";
        /// <summary>GeoJSON order: [longitude, latitude].</summary>
        public List<double[]> Coordinates { get; init; } = new();
    }

    /// <summary>Build corridor GeoJSON or null when traverse legs are unavailable.</summary>
    public static GeoJsonFeatureCollection? TryBuildCorridor(CaveProjectDocument? project) =>
        TryBuildCorridorDetailed(project).Corridor;

    /// <summary>
    /// Builds corridor GeoJSON from traverse legs. Without entrance GPS, anchors the shape at
    /// <see cref="DefaultPreviewAnchorLat"/> / <see cref="DefaultPreviewAnchorLon"/> for map preview.
    /// </summary>
    public static CorridorBuildResult TryBuildCorridorDetailed(CaveProjectDocument? project)
    {
        if (project == null)
            return new CorridorBuildResult(null, false);

        var provisional = false;
        double entLat;
        double entLon;
        if (TryReadEntrance(project, out entLat, out entLon))
        {
            /* georeferenced entrance */
        }
        else
        {
            entLat = DefaultPreviewAnchorLat;
            entLon = DefaultPreviewAnchorLon;
            provisional = true;
        }

        var traverseLegs = project.Shots.Where(s => s.IsTraverseLeg).ToList();
        if (traverseLegs.Count == 0)
            return new CorridorBuildResult(null, false);

        var declination = project.SurveyCalibrationProfile?.MagneticDeclinationAppliedDeg ?? 0f;
        var stationGps = BuildStationCoordsGps(project.Shots, entLat, entLon, declination);
        if (stationGps.Count == 0)
            return new CorridorBuildResult(null, false);

        var chains = SplitTraverseStationChains(traverseLegs);
        var features = new List<GeoJsonLineFeature>();
        foreach (var chain in chains)
        {
            var coords = chain
                .Where(stationGps.ContainsKey)
                .Select(st => stationGps[st])
                .ToList();
            if (coords.Count < 2)
                continue;

            features.Add(new GeoJsonLineFeature
            {
                Geometry = new GeoJsonLineGeometry
                {
                    Coordinates = coords.Select(p => new[] { p.Lon, p.Lat }).ToList(),
                },
            });
        }

        if (features.Count == 0)
            return new CorridorBuildResult(null, false);

        return new CorridorBuildResult(new GeoJsonFeatureCollection { Features = features }, provisional);
    }

    internal static bool TryReadEntrance(CaveProjectDocument project, out double lat, out double lon)
    {
        lat = lon = 0;
        if (project.Lat is not { } la || la <= -90 || la >= 90)
            return false;
        if (project.Lon is not { } lo || lo <= -180 || lo >= 180)
            return false;
        if (Math.Abs(la) < 1e-12 && Math.Abs(lo) < 1e-12)
            return false;
        lat = la;
        lon = lo;
        return true;
    }

    internal static Dictionary<string, LatLonPoint> BuildStationCoordsGps(
        IReadOnlyList<ShotRecord> shots,
        double entranceLat,
        double entranceLon,
        float declinationDeg)
    {
        var coords = new Dictionary<string, LatLonPoint>(StringComparer.OrdinalIgnoreCase);
        if (shots.Count == 0)
            return coords;

        var firstSt = shots[0].FromStation;
        if (string.IsNullOrWhiteSpace(firstSt))
            return coords;

        coords[firstSt] = new LatLonPoint(entranceLat, entranceLon);

        foreach (var shot in shots)
        {
            if (!shot.IsTraverseLeg)
                continue;
            if (!coords.TryGetValue(shot.FromStation, out var from))
                continue;

            var to = OffsetLatLngMeters(from, shot.Azimuth + declinationDeg, shot.Clino, shot.Distance);
            coords[shot.ToStation] = to;
        }

        return coords;
    }

    internal static LatLonPoint OffsetLatLngMeters(LatLonPoint from, float azimuthDeg, float clinoDeg, float distanceM)
    {
        var azRad = azimuthDeg * (Math.PI / 180.0);
        var clRad = clinoDeg * (Math.PI / 180.0);
        var hDist = distanceM * Math.Cos(clRad);

        var lat1 = from.Lat * (Math.PI / 180.0);
        var lon1 = from.Lon * (Math.PI / 180.0);
        var lat2 = Math.Asin(
            Math.Sin(lat1) * Math.Cos(hDist / EarthRadiusM) +
            Math.Cos(lat1) * Math.Sin(hDist / EarthRadiusM) * Math.Cos(azRad));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(azRad) * Math.Sin(hDist / EarthRadiusM) * Math.Cos(lat1),
            Math.Cos(hDist / EarthRadiusM) - Math.Sin(lat1) * Math.Sin(lat2));

        return new LatLonPoint(lat2 * (180.0 / Math.PI), lon2 * (180.0 / Math.PI));
    }

    /// <summary>Port of Android <c>splitTraverseStationChains</c>.</summary>
    internal static List<List<string>> SplitTraverseStationChains(IReadOnlyList<ShotRecord> traverseLegs)
    {
        var chains = new List<List<string>>();
        if (traverseLegs.Count == 0)
            return chains;

        var cur = new List<string>();
        foreach (var s in traverseLegs)
        {
            if (cur.Count == 0)
            {
                cur.Add(s.FromStation);
                cur.Add(s.ToStation);
            }
            else if (string.Equals(cur[^1], s.FromStation, StringComparison.OrdinalIgnoreCase))
            {
                cur.Add(s.ToStation);
            }
            else
            {
                chains.Add(cur);
                cur = new List<string> { s.FromStation, s.ToStation };
            }
        }

        if (cur.Count > 0)
            chains.Add(cur);

        return chains;
    }
}