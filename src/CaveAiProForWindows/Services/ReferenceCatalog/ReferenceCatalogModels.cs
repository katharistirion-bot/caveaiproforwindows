using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>
/// Tolerant converter: accepts numeric osmId values and silently returns null for string values
/// (curated catalog entries use refCode, not a numeric OSM id).
/// </summary>
internal sealed class OsmIdJsonConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number when reader.TryGetInt64(out long v) => v,
            JsonTokenType.Number => (long?)reader.GetDouble(), // float fallback
            JsonTokenType.String => null, // curated refCode string — ignore
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

public sealed class ReferenceCatalogMeta
{
    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("caveCount")]
    public int CaveCount { get; set; }
}

public sealed class ReferenceIndexFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("attribution")]
    public string? Attribution { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("entries")]
    public List<ReferenceCaveIndexEntry> Entries { get; set; } = new();
}

public sealed class ReferenceCaveIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("continent")]
    public string? Continent { get; set; }

    [JsonPropertyName("depthM")]
    public double? DepthM { get; set; }

    [JsonPropertyName("lengthM")]
    public double? LengthM { get; set; }

    [JsonPropertyName("elevationM")]
    public double? ElevationM { get; set; }

    [JsonPropertyName("caveType")]
    public string? CaveType { get; set; }

    [JsonPropertyName("osmType")]
    public string? OsmType { get; set; }

    [JsonPropertyName("osmId")]
    [JsonConverter(typeof(OsmIdJsonConverter))]
    public long? OsmId { get; set; }

    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    [JsonPropertyName("rich")]
    public bool Rich { get; set; }

    public bool HasValidCoordinate() =>
        Lat is >= -90 and <= 90 && Lon is >= -180 and <= 180 && !(Lat == 0 && Lon == 0);
}

public sealed class ReferenceCavePin
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("caveName")]
    public string Name { get; set; } = "";

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("caveNameAlt")]
    public string? CaveNameAlt { get; set; }

    [JsonPropertyName("period")]
    public string? Period { get; set; }

    [JsonPropertyName("significance")]
    public string? Significance { get; set; }

    [JsonPropertyName("finds")]
    public string? Finds { get; set; }

    [JsonPropertyName("heritage")]
    public string? Heritage { get; set; }

    [JsonPropertyName("depthM")]
    public double? DepthM { get; set; }

    [JsonPropertyName("lengthM")]
    public double? LengthM { get; set; }

    [JsonPropertyName("elevationM")]
    public double? ElevationM { get; set; }

    [JsonPropertyName("caveType")]
    public string? CaveType { get; set; }

    [JsonPropertyName("accessNote")]
    public string? AccessNote { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("wikipedia")]
    public string? Wikipedia { get; set; }

    [JsonPropertyName("refCode")]
    public string? RefCode { get; set; }

    [JsonPropertyName("osmType")]
    public string? OsmType { get; set; }

    [JsonPropertyName("osmId")]
    [JsonConverter(typeof(OsmIdJsonConverter))]
    public long? OsmId { get; set; }

    [JsonPropertyName("rich")]
    public bool Rich { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed cave" : Name.Trim();
}

public sealed class ReferenceShardManifest
{
    [JsonPropertyName("shards")]
    public List<ReferenceShardInfo> Shards { get; set; } = new();
}

public sealed class ReferenceShardInfo
{
    [JsonPropertyName("country")]
    public string Country { get; set; } = "";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";
}

public sealed class ReferenceCountryShardFile
{
    [JsonPropertyName("caves")]
    public List<ReferenceCavePin> Caves { get; set; } = new();
}

public sealed class FeaturedReferenceCavesFile
{
    [JsonPropertyName("caves")]
    public List<ReferenceCaveIndexEntry> Caves { get; set; } = new();
}

public sealed class ReferenceCatalogBrowseState
{
    public IReadOnlyList<ReferenceCaveIndexEntry> IndexEntries { get; init; } = Array.Empty<ReferenceCaveIndexEntry>();
    public string? Attribution { get; init; }
    public string Source { get; init; } = "cache";
    public bool FromNetwork { get; init; }
    public bool IsOffline { get; init; }
}
