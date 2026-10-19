using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

public static class ReferenceCatalogSearch
{
    private static readonly Regex TokenSplit = new(@"[\s,;]+", RegexOptions.Compiled);

    public static IReadOnlyList<string> ListCountries(IEnumerable<ReferenceCaveIndexEntry> entries) =>
        entries
            .Select(e => e.Country?.Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

    public static IReadOnlyList<ReferenceCaveIndexEntry> Filter(
        IReadOnlyList<ReferenceCaveIndexEntry> entries,
        string? searchText,
        string? countryFilter,
        double? nearLat,
        double? nearLon,
        double nearRadiusKm = 50,
        int maxResults = 500,
        bool richMetadataOnly = false)
    {
        IEnumerable<ReferenceCaveIndexEntry> q = entries;

        if (!string.IsNullOrWhiteSpace(countryFilter) &&
            !string.Equals(countryFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(e => string.Equals(e.Country, countryFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (richMetadataOnly)
        {
            q = q.Where(ReferenceCatalogDisplay.HasRichMetadata);
        }

        var searchOrigin = TryParseSearchCoordinates(searchText);
        var tokens = searchOrigin is null ? Tokenize(searchText) : [];
        var effectiveLat = searchOrigin?.Lat ?? nearLat;
        var effectiveLon = searchOrigin?.Lon ?? nearLon;
        var hasNear = effectiveLat.HasValue && effectiveLon.HasValue;

        if (tokens.Count == 0 && !hasNear)
            return q.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Take(maxResults).ToList();

        if (tokens.Count > 0)
        {
            var minLen = tokens.All(t => t.Length >= 2) || hasNear || !string.IsNullOrWhiteSpace(countryFilter);
            if (!minLen && tokens.Count == 1 && tokens[0].Length < 2)
                return [];

            q = q.Where(e => MatchesTokens(e, tokens));
        }

        if (hasNear)
        {
            q = q
                .Select(e => (Entry: e, Dist: GeoHaversine.DistanceKm(effectiveLat!.Value, effectiveLon!.Value, e.Lat, e.Lon)))
                .Where(x => x.Dist <= nearRadiusKm)
                .OrderBy(x => x.Dist)
                .Select(x => x.Entry);
        }
        else
        {
            q = q.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        return q.Take(maxResults).ToList();
    }

    public static IReadOnlyList<(ReferenceCaveIndexEntry Entry, double DistanceKm)> FilterWithDistance(
        IReadOnlyList<ReferenceCaveIndexEntry> entries,
        string? searchText,
        string? countryFilter,
        double nearLat,
        double nearLon,
        double nearRadiusKm = 50,
        int maxResults = 500)
    {
        var filtered = Filter(entries, searchText, countryFilter, nearLat, nearLon, nearRadiusKm, maxResults);
        return filtered
            .Select(e => (e, GeoHaversine.DistanceKm(nearLat, nearLon, e.Lat, e.Lon)))
            .ToList();
    }

    public static bool ShouldRunSearch(string? searchText, string? countryFilter, bool nearMe, bool richMetadataOnly = false) =>
        nearMe ||
        richMetadataOnly ||
        TryParseSearchCoordinates(searchText) is not null ||
        !string.IsNullOrWhiteSpace(countryFilter) && !string.Equals(countryFilter, "All", StringComparison.OrdinalIgnoreCase) ||
        Tokenize(searchText).Any(t => t.Length >= 2);

    public readonly record struct SearchLatLon(double Lat, double Lon);

    /// <summary>Parse catalog search text as WGS84 lat/lon. Fail closed on garbage / 0,0.</summary>
    public static SearchLatLon? TryParseSearchCoordinates(string? raw)
    {
        try
        {
            var q = Regex.Replace((raw ?? "").Trim(), @"[()\[\]]", " ");
            q = Regex.Replace(q, @"\s+", " ");
            if (q.Length == 0)
                return null;

            var hemAfter = Regex.Match(
                q,
                @"^([+-]?\d+(?:[.,]\d+)?)\s*°?\s*([NnSs])\s*[,;\s]+\s*([+-]?\d+(?:[.,]\d+)?)\s*°?\s*([EeWw])\s*$");
            if (hemAfter.Success)
            {
                return FinishPair(
                    ParseDecimalToken(hemAfter.Groups[1].Value),
                    hemAfter.Groups[2].Value,
                    ParseDecimalToken(hemAfter.Groups[3].Value),
                    hemAfter.Groups[4].Value);
            }

            var hemBefore = Regex.Match(
                q,
                @"^([NnSs])\s*([+-]?\d+(?:[.,]\d+)?)\s*[,;\s]+\s*([EeWw])\s*([+-]?\d+(?:[.,]\d+)?)\s*$");
            if (hemBefore.Success)
            {
                return FinishPair(
                    ParseDecimalToken(hemBefore.Groups[2].Value),
                    hemBefore.Groups[1].Value,
                    ParseDecimalToken(hemBefore.Groups[4].Value),
                    hemBefore.Groups[3].Value);
            }

            if (Regex.IsMatch(q, @"[A-Za-z]"))
                return null;

            string left;
            string right;
            if (q.Contains(';'))
            {
                var parts = q.Split(';');
                if (parts.Length != 2)
                    return null;
                left = parts[0];
                right = parts[1];
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d+\.\d+\s*,\s*[+-]?\d+\.\d+$"))
            {
                var parts = q.Split(',');
                left = parts[0];
                right = parts[1];
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d+,\d+\s+[+-]?\d+,\d+$"))
            {
                var parts = q.Split(' ');
                left = parts[0];
                right = parts[1];
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d+\.\d+\s+[+-]?\d+\.\d+$"))
            {
                var parts = q.Split(' ');
                left = parts[0];
                right = parts[1];
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d{1,2},\d+\s*,\s*[+-]?\d{1,3},\d+$"))
            {
                var m = Regex.Match(q, @"^([+-]?\d{1,2},\d+)\s*,\s*([+-]?\d{1,3},\d+)$");
                if (!m.Success)
                    return null;
                left = m.Groups[1].Value;
                right = m.Groups[2].Value;
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d+(?:\.\d+)?\s+[+-]?\d+(?:\.\d+)?$"))
            {
                var parts = q.Split(' ');
                left = parts[0];
                right = parts[1];
            }
            else if (Regex.IsMatch(q, @"^[+-]?\d+(?:\.\d+)?\s*,\s+[+-]?\d+(?:\.\d+)?$"))
            {
                var parts = q.Split(',');
                if (parts.Length != 2)
                    return null;
                left = parts[0];
                right = parts[1];
            }
            else
            {
                return null;
            }

            if (!PairHasDecimalPrecision(left, right))
                return null;
            return FinishPair(ParseDecimalToken(left), null, ParseDecimalToken(right), null);
        }
        catch
        {
            return null;
        }
    }

    private static bool PairHasDecimalPrecision(string left, string right)
    {
        static bool HasDecimal(string token) =>
            token.Trim().Contains('.') || token.Trim().Contains(',');
        return HasDecimal(left) || HasDecimal(right);
    }

    private static double? ParseDecimalToken(string? token)
    {
        var t = (token ?? "").Trim();
        if (t.Length == 0 || t.Any(ch => "0123456789+-.,".IndexOf(ch) < 0))
            return null;
        var hasDot = t.Contains('.');
        var hasComma = t.Contains(',');
        if (hasDot && hasComma)
            return null;
        var normalized = hasComma ? t.Replace(',', '.') : t;
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return null;
        return n;
    }

    private static SearchLatLon? FinishPair(double? lat, string? latHem, double? lon, string? lonHem)
    {
        if (lat is null || lon is null)
            return null;
        var la = lat.Value;
        var lo = lon.Value;
        if (latHem is "S" or "s") la = -Math.Abs(la);
        else if (latHem is "N" or "n") la = Math.Abs(la);
        if (lonHem is "W" or "w") lo = -Math.Abs(lo);
        else if (lonHem is "E" or "e") lo = Math.Abs(lo);
        if (!ReferenceCatalogGeolocation.IsValidCoordinate(la, lo))
            return null;
        return new SearchLatLon(la, lo);
    }

    private static List<string> Tokenize(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return [];

        return TokenSplit
            .Split(searchText.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static bool MatchesTokens(ReferenceCaveIndexEntry e, IReadOnlyList<string> tokens)
    {
        var hay = BuildHaystack(e);
        foreach (var t in tokens)
        {
            if (!hay.Contains(t, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string BuildHaystack(ReferenceCaveIndexEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Name).Append(' ');
        if (!string.IsNullOrWhiteSpace(e.Country)) sb.Append(e.Country).Append(' ');
        if (!string.IsNullOrWhiteSpace(e.Region)) sb.Append(e.Region).Append(' ');
        if (!string.IsNullOrWhiteSpace(e.Preview)) sb.Append(e.Preview).Append(' ');
        if (e.OsmId.HasValue) sb.Append(e.OsmId.Value.ToString(CultureInfo.InvariantCulture)).Append(' ');
        return sb.ToString().ToLowerInvariant();
    }
}

public static class GeoHaversine
{
    private const double EarthRadiusKm = 6371.0;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double DegreesToRadians(double deg) => deg * Math.PI / 180.0;
}

public static class ReferenceCatalogCountrySlug
{
    public static string Slugify(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
            return "unknown";

        var normalized = country.ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 72)
            slug = slug[..72].TrimEnd('-');
        return string.IsNullOrEmpty(slug) ? "unknown" : slug;
    }
}
