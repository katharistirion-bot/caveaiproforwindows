using System.Globalization;
using System.Text.Json;

namespace CaveAiProForWindows.Services.Auth;

/// <summary>Parses Firestore REST API v1 document JSON (<c>fields</c> map with typed values).</summary>
internal static class FirestoreRestDocumentParser
{
    public static string? GetString(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var el))
            return null;
        if (el.TryGetProperty("stringValue", out var s))
            return s.GetString();
        if (el.TryGetProperty("nullValue", out _))
            return null;
        return null;
    }

    public static long? GetInt64(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var el))
            return null;
        if (el.TryGetProperty("integerValue", out var i))
        {
            var raw = i.GetString();
            if (long.TryParse(raw, out var v))
                return v;
        }

        if (el.TryGetProperty("doubleValue", out var d))
            return (long)d.GetDouble();
        return null;
    }

    public static DateTimeOffset? GetTimestamp(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var el))
            return null;
        if (!el.TryGetProperty("timestampValue", out var ts))
            return null;
        var raw = ts.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return dto.ToUniversalTime();
        return null;
    }

    public static UserEntitlementDocument? TryParseUserEntitlement(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return null;

        var status = GetString(fields, "status") ?? "";
        var until = GetTimestamp(fields, "premiumCloudUntil");
        var source = GetString(fields, "entitlementSource");
        var graceDocId = GetString(fields, "graceDocId");
        return new UserEntitlementDocument(status, until, source, graceDocId);
    }
}
