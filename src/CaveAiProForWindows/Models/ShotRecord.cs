using System.Text.Json;
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

    /// <summary>Android <c>Shot.hasSplayWatch</c> — 12 radial samples replace classic LRUD when <see cref="Radials"/> is full.</summary>
    [JsonPropertyName("hasSplayWatch")]
    public bool HasSplayWatch { get; set; }

    /// <summary>Android Splay Watch: 12 plan radials (m); indices [9]=L, [3]=R, [0]=U, [6]=D match the Android map HUD.</summary>
    [JsonPropertyName("radials")]
    public List<float> Radials { get; set; } = new();

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

    /// <summary>Alternate <c>from</c> key from some exports / imports (merged into <see cref="FromStation"/> after load).</summary>
    [JsonPropertyName("from")]
    public string? FromAlternate { get; set; }

    /// <summary>Alternate <c>to</c> key from some exports / imports (merged into <see cref="ToStation"/> after load).</summary>
    [JsonPropertyName("to")]
    public string? ToAlternate { get; set; }

    [JsonPropertyName("left")]
    public float? LeftAlias { get; set; }

    [JsonPropertyName("right")]
    public float? RightAlias { get; set; }

    [JsonPropertyName("up")]
    public float? UpAlias { get; set; }

    [JsonPropertyName("down")]
    public float? DownAlias { get; set; }

    /// <summary>Unmapped per-shot JSON (e.g. nested <c>lrud</c> object) — read by <see cref="ShotImportNormalizer"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>True when <c>toStation</c> is a splay destination (Android uses <c>"-"</c>).</summary>
    public static bool IsSplayDestination(string? toStation)
    {
        if (string.IsNullOrWhiteSpace(toStation))
            return false;
        var t = toStation.Trim();
        return t is "-" or "\u2013" or "\u2014" or "\u2212"; // ASCII hyphen, en dash, em dash, minus sign
    }

    /// <summary>Main traverse leg vs splay side-shot (toStation <c>"-"</c>).</summary>
    public bool IsTraverseLeg => !IsSplayDestination(ToStation);

    /// <summary>
    /// Effective L/R/U/D in metres for plan geometry: classic <c>l/r/u/d</c> when any are set; otherwise 12-point
    /// Splay Watch radials when present (same mapping as CaveAI Pro Android: [9]=L, [3]=R, [0]=U, [6]=D).
    /// </summary>
    public (float L, float R, float U, float D) EffectivePlanLrud()
    {
        const float eps = 1e-4f;
        var classicEmpty = Math.Abs(L) < eps && Math.Abs(R) < eps && Math.Abs(U) < eps && Math.Abs(D) < eps;
        // Android may ship 12 plan radials while l/r/u/d are zero; never override explicit classic LRUD.
        if (Radials.Count >= 12 && classicEmpty)
            return (Radials[9], Radials[3], Radials[0], Radials[6]);

        return (L, R, U, D);
    }
}
