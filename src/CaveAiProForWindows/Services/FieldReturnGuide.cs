namespace CaveAiProForWindows.Services;

/// <summary>Distance and bearing from a GPS fix back to the cave entrance.</summary>
public static class FieldReturnGuide
{
    public const int GpsLostAfterSeconds = 25;

    public static double DistanceMeters(double fromLat, double fromLon, double toLat, double toLon)
    {
        const double radius = 6_371_000.0;
        var lat1 = fromLat * Math.PI / 180.0;
        var lat2 = toLat * Math.PI / 180.0;
        var dLat = (toLat - fromLat) * Math.PI / 180.0;
        var dLon = (toLon - fromLon) * Math.PI / 180.0;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * radius * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    /// <summary>Initial compass bearing in degrees, 0 north, 90 east.</summary>
    public static double BearingDegrees(double fromLat, double fromLon, double toLat, double toLon)
    {
        var lat1 = fromLat * Math.PI / 180.0;
        var lat2 = toLat * Math.PI / 180.0;
        var dLon = (toLon - fromLon) * Math.PI / 180.0;
        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var deg = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (deg + 360.0) % 360.0;
    }

    public static bool GpsLost(DateTimeOffset fixUtc, DateTimeOffset nowUtc) =>
        nowUtc - fixUtc > TimeSpan.FromSeconds(GpsLostAfterSeconds);
}
