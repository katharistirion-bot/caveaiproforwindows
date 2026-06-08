using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Helpers for Firestore REST v1 <c>fields</c> map encoding.</summary>
public static class FirestoreFieldBuilder
{
    public static Dictionary<string, object> BuildFields(IEnumerable<KeyValuePair<string, object?>> values)
    {
        var fields = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (value == null)
                continue;
            fields[key] = EncodeValue(value);
        }

        return fields;
    }

    public static object EncodeValue(object value) => value switch
    {
        string s => new Dictionary<string, object> { ["stringValue"] = s },
        bool b => new Dictionary<string, object> { ["booleanValue"] = b },
        int i => new Dictionary<string, object> { ["integerValue"] = i.ToString() },
        long l => new Dictionary<string, object> { ["integerValue"] = l.ToString() },
        float f => new Dictionary<string, object> { ["doubleValue"] = f },
        double d => new Dictionary<string, object> { ["doubleValue"] = d },
        DateTimeOffset dto => new Dictionary<string, object>
        {
            ["timestampValue"] = dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"),
        },
        IEnumerable<string> urls => EncodeStringArray(urls),
        _ => new Dictionary<string, object> { ["stringValue"] = value.ToString() ?? "" },
    };

    /// <summary>Firestore REST array of string values (e.g. <c>cartographyImageUrls</c>).</summary>
    public static Dictionary<string, object> EncodeStringArray(IEnumerable<string> values)
    {
        var items = values
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => new Dictionary<string, object> { ["stringValue"] = u.Trim() })
            .ToList();
        return new Dictionary<string, object>
        {
            ["arrayValue"] = new Dictionary<string, object> { ["values"] = items },
        };
    }
}

/// <summary>Firestore PATCH body wrapper.</summary>
public sealed class FirestoreDocumentPatch
{
    [JsonPropertyName("fields")]
    public Dictionary<string, object> Fields { get; init; } = new();
}
