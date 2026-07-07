using CaveAiProForWindows.Models;
using Windows.Devices.Geolocation;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Resolves Near me origin for the reference catalog (device GPS, then optional project entrance).</summary>
public static class ReferenceCatalogGeolocation
{
    public const double NearMeMaxAcceptableAccuracyM = 100;
    public const double NearMeWarnAccuracyM = 50;

    public sealed record NearMeOrigin(double Lat, double Lon, string SourceLabel, double? AccuracyMeters = null);

    public sealed record NearMeLocationResult(bool Ok, NearMeOrigin? Origin, string? Error, string? Warning);

    public static bool IsValidCoordinate(double lat, double lon) =>
        lat is >= -90 and <= 90 &&
        lon is >= -180 and <= 180 &&
        !(lat == 0 && lon == 0);

    public static NearMeLocationResult ValidateNearMeOrigin(NearMeOrigin? origin)
    {
        if (origin is null)
            return new NearMeLocationResult(false, null, "GPS location unavailable — allow location access or use project entrance.", null);

        if (!IsValidCoordinate(origin.Lat, origin.Lon))
            return new NearMeLocationResult(false, null, "Invalid GPS coordinates — try again in open sky.", null);

        var acc = origin.AccuracyMeters;
        if (acc is > NearMeMaxAcceptableAccuracyM)
        {
            return new NearMeLocationResult(
                false,
                null,
                $"GPS accuracy is poor (~{Math.Round(acc.Value)} m). Move to open sky and refresh Near me.",
                null);
        }

        if (acc is > NearMeWarnAccuracyM)
        {
            return new NearMeLocationResult(
                true,
                origin,
                null,
                $"GPS accuracy is moderate (~{Math.Round(acc.Value)} m). Results may be offset until you get a better fix.");
        }

        return new NearMeLocationResult(true, origin, null, null);
    }

    public static async Task<NearMeLocationResult> TryGetDeviceLocationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
            if (access != GeolocationAccessStatus.Allowed)
                return new NearMeLocationResult(false, null, "Location access denied — enable Windows location for this app.", null);

            var geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.High,
                DesiredAccuracyInMeters = 25,
            };

            var position = await geolocator.GetGeopositionAsync(
                    maximumAge: TimeSpan.Zero,
                    timeout: TimeSpan.FromSeconds(15))
                .AsTask(cancellationToken);

            var lat = position.Coordinate.Latitude;
            var lon = position.Coordinate.Longitude;
            if (!IsValidCoordinate(lat, lon))
                return new NearMeLocationResult(false, null, "Invalid GPS coordinates — try again in open sky.", null);

            var accuracy = position.Coordinate.Accuracy;
            var origin = new NearMeOrigin(lat, lon, "Windows location (high accuracy)", accuracy);
            return ValidateNearMeOrigin(origin);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new NearMeLocationResult(false, null, "GPS fix failed — try again near a window or outdoors.", null);
        }
    }

    public static NearMeOrigin? TryGetProjectEntrance(CaveProjectDocument? project)
    {
        if (project is not { Lat: { } la, Lon: { } lo })
            return null;
        if (!IsValidCoordinate(la, lo))
            return null;
        return new NearMeOrigin(la, lo, $"Project entrance ({project.Name})", null);
    }
}
