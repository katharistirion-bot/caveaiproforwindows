using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

public sealed class FieldTripDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Field trip";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("stops")]
    public List<FieldTripStop> Stops { get; set; } = new();

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class FieldTripStop
{
    [JsonPropertyName("referenceId")]
    public string ReferenceId { get; set; } = "";

    /// <summary>Firestore <c>published_caves</c> doc id when stop is a community cave (field trip share v1 <c>k=p</c>).</summary>
    [JsonPropertyName("communityDocId")]
    public string? CommunityDocId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("depthM")]
    public double? DepthM { get; set; }

    [JsonPropertyName("lengthM")]
    public double? LengthM { get; set; }
}

public sealed class FieldTripStoreFile
{
    [JsonPropertyName("trips")]
    public List<FieldTripDocument> Trips { get; set; } = new();
}
