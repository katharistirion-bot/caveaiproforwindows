using System.Globalization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.FieldTrip;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

public static class ReferenceCatalogShareUrls
{
    public static string BuildShareUrl(ReferenceCaveIndexEntry entry)
    {
        var id = Uri.EscapeDataString(entry.Id.Trim());
        var country = ReferenceCatalogCountrySlug.Slugify(entry.Country);
        return $"{FieldTripShareCodec.SiteOrigin}/cave/ref/{id}?country={country}";
    }

    /// <summary>Opens Explore terrain on the web map centered on a reference cave.</summary>
    public static string BuildExploreTerrainUrl(ReferenceCaveIndexEntry entry, string preset = "terrain", int? zoom = null)
    {
        if (!ReferenceCatalogGeolocation.IsValidCoordinate(entry.Lat, entry.Lon))
            return PublicLibraryCatalog.WebExploreMapUrl;
        return BuildExploreTerrainUrlForCoordinates(
            [(entry.Lat, entry.Lon, entry.Country)],
            preset,
            fallbackZoom: zoom ?? 13);
    }

    /// <summary>Explore terrain with terrain preset (hydrology + karst) zoomed for OSM scouting near a cave.</summary>
    public static string BuildExploreHydrologyScoutUrl(ReferenceCaveIndexEntry entry) =>
        BuildExploreTerrainUrl(entry, preset: "terrain", zoom: 14);

    /// <summary>Explore terrain URL framing two reference catalog caves (compare on terrain).</summary>
    public static string BuildExploreCompareTerrainUrl(ReferenceCaveIndexEntry left, ReferenceCaveIndexEntry right, string preset = "terrain") =>
        BuildExploreTerrainUrlForCoordinates(
            [(left.Lat, left.Lon, left.Country), (right.Lat, right.Lon, right.Country)],
            preset,
            fallbackZoom: 10);

    /// <summary>Explore terrain URL framing multiple field-trip stops.</summary>
    public static string BuildExploreTerrainUrlForStops(IReadOnlyList<FieldTripStop> stops, string preset = "terrain")
    {
        if (stops == null || stops.Count == 0)
            return PublicLibraryCatalog.WebExploreMapUrl;
        var coords = stops
            .Where(s => ReferenceCatalogGeolocation.IsValidCoordinate(s.Lat, s.Lon))
            .Select(s => (s.Lat, s.Lon, (string?)s.Country))
            .ToList();
        if (coords.Count == 0)
            return PublicLibraryCatalog.WebExploreMapUrl;
        return BuildExploreTerrainUrlForCoordinates(coords, preset, fallbackZoom: 12);
    }

    private static string BuildExploreTerrainUrlForCoordinates(
        IReadOnlyList<(double Lat, double Lon, string? Country)> coords,
        string preset,
        int fallbackZoom)
    {
        if (coords == null || coords.Count == 0)
            return PublicLibraryCatalog.WebExploreMapUrl;

        var lats = coords.Select(c => c.Lat).ToList();
        var lons = coords.Select(c => c.Lon).ToList();
        var centerLat = (lats.Min() + lats.Max()) / 2;
        var centerLon = (lons.Min() + lons.Max()) / 2;
        var span = Math.Max(lats.Max() - lats.Min(), lons.Max() - lons.Min());
        var zoom = coords.Count == 1
            ? fallbackZoom
            : span switch
            {
                > 8 => 5,
                > 3 => 6,
                > 1 => 8,
                > 0.3 => 10,
                _ => fallbackZoom,
            };

        var countries = coords
            .Select(c => ReferenceCatalogCountrySlug.Slugify(c.Country))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var country = countries.Count == 1 ? countries[0] : coords[0].Country is { } c0 ? ReferenceCatalogCountrySlug.Slugify(c0) : "";

        var lat = centerLat.ToString(CultureInfo.InvariantCulture);
        var lon = centerLon.ToString(CultureInfo.InvariantCulture);
        var presetPart = string.IsNullOrWhiteSpace(preset) ? "terrain" : preset.Trim().ToLowerInvariant();
        var countryPart = string.IsNullOrWhiteSpace(country) ? "" : $"&country={country}";
        return $"{FieldTripShareCodec.SiteOrigin}/map?view=explore&preset={Uri.EscapeDataString(presetPart)}&lat={lat}&lon={lon}&zoom={zoom}{countryPart}";
    }

    /// <summary>Opens Cave AI Pro on Android to start field survey (<c>?action=survey</c>).</summary>
    public static string BuildSurveyStartUrl(ReferenceCaveIndexEntry entry)
    {
        var id = Uri.EscapeDataString(entry.Id.Trim());
        var country = ReferenceCatalogCountrySlug.Slugify(entry.Country);
        return $"{FieldTripShareCodec.SiteOrigin}/cave/ref/{id}?action=survey&country={country}";
    }

    /// <summary>Parses <c>https://www.caveaipro.com/cave/ref/{id}?country=</c> share URLs.</summary>
    public static bool TryParseReferenceShareUrl(string? urlOrText, out string? referenceId, out string? countryHint)
    {
        referenceId = null;
        countryHint = null;
        if (string.IsNullOrWhiteSpace(urlOrText))
            return false;

        if (!Uri.TryCreate(urlOrText.Trim(), UriKind.Absolute, out var uri))
            return false;

        var host = uri.Host.Trim().ToLowerInvariant();
        if (host is not ("www.caveaipro.com" or "caveaipro.com"))
            return false;

        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        if (!path.StartsWith("cave/ref/", StringComparison.Ordinal))
            return false;

        var id = Uri.UnescapeDataString(path["cave/ref/".Length..].Split('/')[0]).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return false;

        referenceId = id;
        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            if (string.Equals(part[..eq], "country", StringComparison.OrdinalIgnoreCase))
            {
                countryHint = Uri.UnescapeDataString(part[(eq + 1)..]).Trim();
                break;
            }
        }
        return true;
    }
}
