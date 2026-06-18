using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.FieldTrip;

/// <summary>Cross-platform field trip share v1 — parity with web <c>fieldTripShare.js</c>.</summary>
public static class FieldTripShareCodec
{
    public const string SiteOrigin = "https://www.caveaipro.com";
    private const int InlineJsonMaxLength = 1800;

    public static FieldTripSharePayloadV1 BuildPayload(IReadOnlyList<FieldTripStop> stops) =>
        new()
        {
            V = 1,
            Stops = EncodeStops(stops),
        };

    public static string BuildShareUrl(IReadOnlyList<FieldTripStop> stops, string? origin = null)
    {
        var baseUrl = (origin ?? SiteOrigin).TrimEnd('/');
        var json = JsonSerializer.Serialize(BuildPayload(stops));
        if (json.Length <= InlineJsonMaxLength)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return $"{baseUrl}/map?view=fieldtrip#trip={encoded}";
        }

        return $"{baseUrl}/map?view=fieldtrip";
    }

    public static FieldTripSharePayloadV1? TryParseFromUrl(string? urlOrText)
    {
        if (string.IsNullOrWhiteSpace(urlOrText))
            return null;

        var text = urlOrText.Trim();
        var hashIdx = text.IndexOf('#');
        if (hashIdx >= 0)
        {
            var fromHash = TryParseFromHash(text[(hashIdx + 1)..]);
            if (fromHash != null)
                return fromHash;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.StartsWith("trip=", StringComparison.Ordinal))
                {
                    var fromQuery = TryDecodeTripPart(part["trip=".Length..]);
                    if (fromQuery != null)
                        return fromQuery;
                }
            }
        }

        return null;
    }

    public static FieldTripSharePayloadV1? TryParseFromHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return null;

        var m = System.Text.RegularExpressions.Regex.Match(hash, @"trip=([^&]+)");
        return m.Success ? TryDecodeTripPart(m.Groups[1].Value) : null;
    }

    public static List<FieldTripStop> ToFieldTripStops(FieldTripSharePayloadV1 payload)
    {
        var list = new List<FieldTripStop>();
        foreach (var s in payload.Stops)
        {
            if (!s.La.HasValue || !s.Lo.HasValue)
                continue;

            var stop = new FieldTripStop
            {
                Name = string.IsNullOrWhiteSpace(s.N) ? "Site" : s.N.Trim(),
                Lat = s.La.Value,
                Lon = s.Lo.Value,
                Country = string.IsNullOrWhiteSpace(s.C) ? null : s.C.Trim(),
            };

            if (string.Equals(s.K, "p", StringComparison.OrdinalIgnoreCase))
            {
                stop.CommunityDocId = s.I ?? "";
            }
            else
            {
                stop.ReferenceId = s.I ?? "";
            }

            list.Add(stop);
        }

        return list;
    }

    private static FieldTripSharePayloadV1? TryDecodeTripPart(string encodedPart)
    {
        try
        {
            var decoded = Uri.UnescapeDataString(encodedPart);
            var jsonBytes = Convert.FromBase64String(decoded);
            var json = Encoding.UTF8.GetString(jsonBytes);
            var doc = JsonSerializer.Deserialize<FieldTripSharePayloadV1>(json);
            return doc is { Stops.Count: > 0 } ? doc : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<FieldTripShareStopV1> EncodeStops(IReadOnlyList<FieldTripStop> stops)
    {
        var list = new List<FieldTripShareStopV1>();
        foreach (var s in stops)
        {
            if (s.Lat is < -90 or > 90 || s.Lon is < -180 or > 180)
                continue;

            var isCommunity = !string.IsNullOrWhiteSpace(s.CommunityDocId);
            var id = isCommunity ? s.CommunityDocId!.Trim() : s.ReferenceId.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var name = string.IsNullOrWhiteSpace(s.Name) ? "Site" : s.Name.Trim();
            if (name.Length > 80)
                name = name[..80];

            list.Add(new FieldTripShareStopV1
            {
                K = isCommunity ? "p" : "r",
                I = id,
                N = name,
                La = Math.Round(s.Lat, 5),
                Lo = Math.Round(s.Lon, 5),
                C = s.Country ?? "",
            });
        }

        return list;
    }
}

public sealed class FieldTripSharePayloadV1
{
    [JsonPropertyName("v")]
    public int V { get; set; } = 1;

    [JsonPropertyName("stops")]
    public List<FieldTripShareStopV1> Stops { get; set; } = new();
}

public sealed class FieldTripShareStopV1
{
    [JsonPropertyName("k")]
    public string? K { get; set; }

    [JsonPropertyName("i")]
    public string? I { get; set; }

    [JsonPropertyName("n")]
    public string? N { get; set; }

    [JsonPropertyName("la")]
    public double? La { get; set; }

    [JsonPropertyName("lo")]
    public double? Lo { get; set; }

    [JsonPropertyName("c")]
    public string? C { get; set; }
}
