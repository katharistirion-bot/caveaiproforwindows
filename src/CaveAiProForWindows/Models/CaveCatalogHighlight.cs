using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Android <c>CaveCatalogHighlight</c> inside <c>KnownCave.catalogHighlights</c>.</summary>
public sealed class CaveCatalogHighlight
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("analysis")]
    public string Analysis { get; set; } = "";

    [JsonPropertyName("stationTag")]
    public string StationTag { get; set; } = "";

    [JsonPropertyName("isGeminiSourced")]
    public bool IsGeminiSourced { get; set; }
}
