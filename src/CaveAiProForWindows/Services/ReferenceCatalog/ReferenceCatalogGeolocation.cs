using CaveAiProForWindows.Models;
using Windows.Devices.Geolocation;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Resolves Near me origin for the reference catalog (device GPS, then optional project entrance).</summary>
public static class ReferenceCatalogGeolocation
{
    public sealed record NearMeOrigin(double Lat, double Lon, string SourceLabel);

    public static bool IsValidCoordinate(double lat, double lon) =>
        lat is >= -90 and <= 90 &&
        lon is >= -180 and <= 180 &&
        !(lat == 0 && lon == 0);

    public static async Task<NearMeOrigin?> TryGetDeviceLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
            if (access != GeolocationAccessStatus.Allowed)
                return null;

            var geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.Default,
                DesiredAccuracyInMeters = 100,
            };

            var position = await geolocator.GetGeopositionAsync(
                    maximumAge: TimeSpan.FromMinutes(5),
                    timeout: TimeSpan.FromSeconds(15))
                .AsTask(cancellationToken);

            var lat = position.Coordinate.Latitude;
            var lon = position.Coordinate.Longitude;
            if (!IsValidCoordinate(lat, lon))
                return null;

            return new NearMeOrigin(lat, lon, "Windows location");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static NearMeOrigin? TryGetProjectEntrance(CaveProjectDocument? project)
    {
        if (project is not { Lat: { } la, Lon: { } lo })
            return null;
        if (!IsValidCoordinate(la, lo))
            return null;
        return new NearMeOrigin(la, lo, $"Project entrance ({project.Name})");
    }
}
