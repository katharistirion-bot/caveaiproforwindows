namespace CaveAiProForWindows.Services;

/// <summary>Raster export resolution preset (logical DIP to physical pixels).</summary>
public enum MapExportQuality
{
    /// <summary>~300 DPI — quick PNG and on-demand screen exports.</summary>
    Standard = 300,

    /// <summary>~600 DPI — print, booklet, batch, and professional map exports.</summary>
    Print = 600,
}

public static class MapExportQualityExtensions
{
    public static double DpiScale(this MapExportQuality quality) => (int)quality / 96.0;
}

public static class MapExportQualityParser
{
    public static MapExportQuality Parse(string? s) =>
        Enum.TryParse<MapExportQuality>(s?.Trim(), true, out var v) ? v : MapExportQuality.Standard;

    public static string ToPersistedString(MapExportQuality v) => v.ToString();
}
