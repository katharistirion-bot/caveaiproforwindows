using System.Globalization;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Android Gson <c>KnownCave</c> (personal Cave Library) — same shape as <c>cave_library.json</c> in backup ZIP.</summary>
public sealed class KnownCaveRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("elevation")]
    public double Elevation { get; set; }

    [JsonPropertyName("depth")]
    public double Depth { get; set; }

    [JsonPropertyName("length")]
    public double Length { get; set; }

    [JsonPropertyName("area")]
    public string Area { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "CAVE";

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; } = 1;

    [JsonPropertyName("imageUri")]
    public string? ImageUri { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("dateAdded")]
    public long DateAdded { get; set; }

    /// <summary>Gson enum name, e.g. <c>REMOTE_HIKE</c>, <c>UNSPECIFIED</c>.</summary>
    [JsonPropertyName("surfaceAccess")]
    public string? SurfaceAccess { get; set; }

    /// <summary>Filled after parse from <c>magneticHints</c> array length (not stored as a JSON field).</summary>
    [JsonIgnore]
    public int MagneticHintsCount { get; set; }

    /// <summary>ZIP or JSON path this row was loaded from (desktop-only).</summary>
    [JsonIgnore]
    public string LoadedFromFile { get; set; } = "";

    public string LatText => Lat.ToString("0.######", CultureInfo.InvariantCulture);

    public string LonText => Lon.ToString("0.######", CultureInfo.InvariantCulture);

    public string DateAddedText
    {
        get
        {
            if (DateAdded <= 0)
                return "";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(DateAdded).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }
    }

    public string DescriptionPreview
    {
        get
        {
            var d = Description.Trim();
            if (d.Length <= 140)
                return d;
            return d[..137] + "…";
        }
    }

    /// <summary>Thumbnail for cards — same resolver rules as survey covers (http / file path).</summary>
    public string CoverUri => ImageUri?.Trim() ?? "";
}
