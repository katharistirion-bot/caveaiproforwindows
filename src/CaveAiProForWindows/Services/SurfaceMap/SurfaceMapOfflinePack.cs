using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.SurfaceMap;

/// <summary>Persisted offline pack metadata for Surface Map (Android parity).</summary>
public sealed class SurfaceMapOfflinePackMeta
{
    public double North { get; set; }
    public double East { get; set; }
    public double South { get; set; }
    public double West { get; set; }
    public long SavedAtMs { get; set; }
    public int ZoomMin { get; set; } = 11;
    public int ZoomMax { get; set; } = 16;

    public bool Covers(double lat, double lon, double marginDeg = 0.01) =>
        lat is >= -90 and <= 90 &&
        lon is >= -180 and <= 180 &&
        lat >= South + marginDeg && lat <= North - marginDeg &&
        lon >= West + marginDeg && lon <= East - marginDeg;

    public string Label()
    {
        var ageH = Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - SavedAtMs) / 3_600_000L);
        return ageH switch
        {
            < 1 => $"Downloaded just now · z{ZoomMin}–{ZoomMax}",
            < 48 => $"Downloaded {ageH}h ago · z{ZoomMin}–{ZoomMax}",
            _ => $"Downloaded {ageH / 24}d ago · z{ZoomMin}–{ZoomMax}",
        };
    }
}

public static class SurfaceMapOfflinePack
{
    public const double HalfSpanDeg = 0.055;
    public const int ZoomMin = 11;
    public const int ZoomMax = 16;

    private static string MetaPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaveAiProForWindows",
        "surface-offline-pack.json");

    public static (double South, double West, double North, double East) BoundingBoxAround(double lat, double lon, double halfSpanDeg = HalfSpanDeg)
    {
        var latSpan = Math.Clamp(halfSpanDeg, 0.01, 0.2);
        var cos = Math.Cos(lat * Math.PI / 180.0);
        var lonSpan = Math.Clamp(halfSpanDeg / Math.Max(0.2, Math.Abs(cos)), 0.01, 0.25);
        return (
            Math.Clamp(lat - latSpan, -90, 90),
            Math.Clamp(lon - lonSpan, -180, 180),
            Math.Clamp(lat + latSpan, -90, 90),
            Math.Clamp(lon + lonSpan, -180, 180));
    }

    public static SurfaceMapOfflinePackMeta? Read()
    {
        try
        {
            if (!File.Exists(MetaPath)) return null;
            return JsonSerializer.Deserialize<SurfaceMapOfflinePackMeta>(File.ReadAllText(MetaPath));
        }
        catch
        {
            return null;
        }
    }

    public static void Write(double south, double west, double north, double east, int zoomMin = ZoomMin, int zoomMax = ZoomMax)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MetaPath)!);
        var meta = new SurfaceMapOfflinePackMeta
        {
            South = south,
            West = west,
            North = north,
            East = east,
            ZoomMin = zoomMin,
            ZoomMax = zoomMax,
            SavedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        File.WriteAllText(MetaPath, JsonSerializer.Serialize(meta));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(MetaPath)) File.Delete(MetaPath);
        }
        catch { }
    }
}
