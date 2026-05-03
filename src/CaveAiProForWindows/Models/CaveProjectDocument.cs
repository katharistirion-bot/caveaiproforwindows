using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Subset of Android <c>CaveProject</c> — matches Gson field names in backup ZIP <c>data.json</c>.</summary>
public sealed class CaveProjectDocument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; }

    [JsonPropertyName("lat")]
    public string? Lat { get; set; }

    [JsonPropertyName("lon")]
    public string? Lon { get; set; }

    [JsonPropertyName("alt")]
    public double Alt { get; set; }

    [JsonPropertyName("shots")]
    public List<ShotRecord> Shots { get; set; } = new();

    /// <summary>Android <c>VectorLine</c> (CaveSurveyModels.kt) — plan/section/profile overlays; Gson may use nested arrays or Pair objects.</summary>
    [JsonPropertyName("vectorLines")]
    public JsonElement? VectorLines { get; set; }

    [JsonPropertyName("rocks")]
    public JsonElement? Rocks { get; set; }

    [JsonPropertyName("fieldCatalogEntries")]
    public JsonElement? FieldCatalogEntries { get; set; }

    public int RocksCount =>
        Rocks is { ValueKind: JsonValueKind.Array } r ? r.GetArrayLength() : 0;

    public int FieldCatalogEntryCount =>
        FieldCatalogEntries is { ValueKind: JsonValueKind.Array } arr ? arr.GetArrayLength() : 0;

    /// <summary>All JSON members not mapped above (sketches, mapSymbols, surfaceLidarRaster, library URIs, trackPoints, etc.).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Set after load when merging multiple files — not part of Gson JSON.</summary>
    [JsonIgnore]
    public string? LoadedFromFile { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(Date) ? Name : $"{Name}  ({Date})";
}
