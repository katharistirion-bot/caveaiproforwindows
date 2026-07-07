using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.FieldTrip;

/// <summary>Cross-platform field trip share v1 — parity with web <c>fieldTripShare.js</c>.</summary>
public static class FieldTripShareCodec
{
    public const string SiteOrigin = "https://www.caveaipro.com";
    private const int InlineJsonMaxLength = 1800;
    private const string FirestoreProjectId = "caveaipro-5950e";
    private const string FirestoreShareCollection = "field_trip_shares";
    private static readonly Regex CloudTripIdRegex = new(@"^ft_[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant);

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

    public static async Task<string> BuildShareUrlAsync(IReadOnlyList<FieldTripStop> stops, string? origin = null, CancellationToken ct = default)
    {
        var baseUrl = (origin ?? SiteOrigin).TrimEnd('/');
        var json = JsonSerializer.Serialize(BuildPayload(stops));
        if (json.Length <= InlineJsonMaxLength)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            return $"{baseUrl}/map?view=fieldtrip#trip={encoded}";
        }

        var tripId = await SaveCloudPayloadAsync(stops, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(tripId))
            return $"{baseUrl}/map?view=fieldtrip&tripId={Uri.EscapeDataString(tripId)}";

        return $"{baseUrl}/map?view=fieldtrip";
    }

    public static string? ExtractTripIdFromUrl(string? urlOrText)
    {
        if (string.IsNullOrWhiteSpace(urlOrText))
            return null;
        if (!Uri.TryCreate(urlOrText.Trim(), UriKind.Absolute, out var uri))
            return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith("tripId=", StringComparison.Ordinal))
                continue;
            var tripId = Uri.UnescapeDataString(part["tripId=".Length..]).Trim();
            return IsCloudTripId(tripId) ? tripId : null;
        }
        return null;
    }

    public static async Task<FieldTripSharePayloadV1?> TryParseFromUrlAsync(string? urlOrText, CancellationToken ct = default)
    {
        var inline = TryParseFromUrl(urlOrText);
        if (inline != null)
            return inline;

        var tripId = ExtractTripIdFromUrl(urlOrText);
        if (string.IsNullOrWhiteSpace(tripId))
            return null;

        return await FetchCloudPayloadAsync(tripId, ct).ConfigureAwait(false);
    }

    private static bool IsCloudTripId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && CloudTripIdRegex.IsMatch(id.Trim());

    private static async Task<FieldTripSharePayloadV1?> FetchCloudPayloadAsync(string tripId, CancellationToken ct)
    {
        var id = tripId.Trim();
        if (!IsCloudTripId(id))
            return null;

        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(FirestoreProjectId)}/databases/(default)/documents/{FirestoreShareCollection}/{Uri.EscapeDataString(id)}";

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ApplyFirebaseAuthHeader(client);
        using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;

        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("fields", out var fields))
                return null;
            if (!fields.TryGetProperty("payloadJson", out var payloadField))
                return null;
            if (!payloadField.TryGetProperty("stringValue", out var stringValue))
                return null;
            var json = stringValue.GetString();
            if (string.IsNullOrWhiteSpace(json))
                return null;
            var payload = JsonSerializer.Deserialize<FieldTripSharePayloadV1>(json);
            return payload is { Stops.Count: > 0 } ? payload : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> SaveCloudPayloadAsync(IReadOnlyList<FieldTripStop> stops, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(BuildPayload(stops));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var token = FirebaseAuthTokenStore.TryLoad();
        if (token == null || string.IsNullOrWhiteSpace(token.Subject))
            return null;

        var idRaw = $"ft_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}_{Guid.NewGuid():N}";
        var id = idRaw.Length <= 48 ? idRaw : idRaw[..48];
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(FirestoreProjectId)}/databases/(default)/documents/{FirestoreShareCollection}?documentId={Uri.EscapeDataString(id)}";

        var firestoreBody = new
        {
            fields = new Dictionary<string, object>
            {
                ["payloadJson"] = new { stringValue = json },
                ["createdAtMs"] = new { integerValue = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
                ["creatorUid"] = new { stringValue = token.Subject },
            },
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        ApplyFirebaseAuthHeader(client);
        using var content = new StringContent(JsonSerializer.Serialize(firestoreBody), Encoding.UTF8, "application/json");
        using var resp = await client.PostAsync(url, content, ct).ConfigureAwait(false);
        return resp.IsSuccessStatusCode ? id : null;
    }

    private static void ApplyFirebaseAuthHeader(HttpClient client)
    {
        var token = FirebaseAuthTokenStore.TryLoad();
        if (token == null || string.IsNullOrWhiteSpace(token.Raw))
            return;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Raw);
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
