using CaveAiProForWindows.Models;
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
