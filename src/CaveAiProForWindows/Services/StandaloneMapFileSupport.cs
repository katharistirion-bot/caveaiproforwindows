using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>Recognises common map / cartography / GIS file types for standalone open and drag-drop.</summary>
public static class StandaloneMapFileSupport
{
    /// <summary>Extensions (with leading dot, lower case) treated as standalone maps.</summary>
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".geotiff",
        ".png", ".jpg", ".jpeg", ".jpe", ".webp", ".bmp", ".gif",
        ".pdf",
        ".svg",
        ".kml", ".kmz",
        ".gpx",
        ".dxf",
        ".jp2", ".j2k",
        ".asc",
        ".mbtiles",
        ".osm",
        ".geojson",
        ".obj", ".mtl",
        ".dem", ".las", ".laz",
        ".wld", // world file often paired with tfw etc. — allow opening sidecar
        ".tfw", ".pgw", ".jgw", ".bpw", ".gfw", ".xyw",
    };

    public static bool HasStandaloneMapExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && Extensions.Contains(ext);
    }

    public static bool IsStandaloneMapFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) && HasStandaloneMapExtension(path);
}
