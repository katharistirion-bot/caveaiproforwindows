using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Two station names that are the same point (Walls #equate / Survex *equate).</summary>
public sealed class StationEquate
{
    [JsonPropertyName("stationA")]
    public string StationA { get; set; } = "";

    [JsonPropertyName("stationB")]
    public string StationB { get; set; } = "";
}
