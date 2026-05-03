using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Subset of Android <c>Shot</c> (CaveSurveyModels.kt) — enough for stats & CSV.</summary>
public sealed class ShotRecord
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("azimuth")]
    public int Azimuth { get; set; }

    [JsonPropertyName("clino")]
    public int Clino { get; set; }

    [JsonPropertyName("distance")]
    public float Distance { get; set; }

    [JsonPropertyName("l")]
    public float L { get; set; }

    [JsonPropertyName("r")]
    public float R { get; set; }

    [JsonPropertyName("u")]
    public float U { get; set; }

    [JsonPropertyName("d")]
    public float D { get; set; }

    [JsonPropertyName("fromStation")]
    public string FromStation { get; set; } = "";

    [JsonPropertyName("toStation")]
    public string ToStation { get; set; } = "";

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("depth")]
    public float Depth { get; set; }

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>Shot photo URIs or ZIP-relative paths (<c>photos/…</c>) from Android <c>data.json</c>.</summary>
    [JsonPropertyName("photos")]
    public List<string> Photos { get; set; } = new();

    [JsonPropertyName("audioMemoUri")]
    public string? AudioMemoUri { get; set; }

    public bool IsTraverseLeg => ToStation != "-";
}
