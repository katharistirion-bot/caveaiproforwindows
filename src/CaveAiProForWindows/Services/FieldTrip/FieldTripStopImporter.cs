using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Services.FieldTrip;

/// <summary>Parses pasted doc ids or share URLs into <see cref="FieldTripStop"/> rows (reference or community).</summary>
public static class FieldTripStopImporter
{
    public static FieldTripStop? TryParseInput(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();

        var tripPayload = FieldTripShareCodec.TryParseFromUrl(trimmed);
        if (tripPayload != null)
        {
            var stops = FieldTripShareCodec.ToFieldTripStops(tripPayload);
            return stops.Count > 0 ? stops[0] : null;
        }

        if (ReferenceCatalogShareUrls.TryParseReferenceShareUrl(trimmed, out var refId, out var countryHint) &&
            !string.IsNullOrWhiteSpace(refId))
        {
            return new FieldTripStop
            {
                ReferenceId = refId!,
                Name = refId!,
                Country = string.IsNullOrWhiteSpace(countryHint) ? null : countryHint,
            };
        }

        if (PublishedCaveUrlParser.TryExtractDocId(trimmed, out var docId))
        {
            return new FieldTripStop
            {
                CommunityDocId = docId,
                Name = docId,
            };
        }

        if (trimmed.Length >= 3 && trimmed.Length <= 128 && !trimmed.Contains(' ') && !trimmed.Contains('\n'))
        {
            return new FieldTripStop
            {
                CommunityDocId = trimmed,
                Name = trimmed,
            };
        }

        return null;
    }
}
