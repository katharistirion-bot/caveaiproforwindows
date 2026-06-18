using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Simple grid clustering for map pins (cap ~250 markers).</summary>
public static class ReferenceCatalogMapClustering
{
    public const int MaxPins = 250;

    public sealed record ClusterPin(
        double Lat,
        double Lon,
        int Count,
        IReadOnlyList<ReferenceCaveIndexEntry> Members);

    public static IReadOnlyList<ClusterPin> Cluster(
        IReadOnlyList<ReferenceCaveIndexEntry> entries,
        double minLat,
        double maxLat,
        double minLon,
        double maxLon,
        int gridCells = 32)
    {
        if (entries.Count == 0)
            return [];

        var latSpan = Math.Max(0.001, maxLat - minLat);
        var lonSpan = Math.Max(0.001, maxLon - minLon);
        var buckets = new Dictionary<(int, int), List<ReferenceCaveIndexEntry>>();

        foreach (var e in entries.Take(MaxPins * 4))
        {
            if (!e.HasValidCoordinate())
                continue;
            var gx = (int)((e.Lon - minLon) / lonSpan * gridCells);
            var gy = (int)((e.Lat - minLat) / latSpan * gridCells);
            gx = Math.Clamp(gx, 0, gridCells - 1);
            gy = Math.Clamp(gy, 0, gridCells - 1);
            var key = (gx, gy);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(e);
        }

        var clusters = buckets.Values
            .Select(members =>
            {
                var lat = members.Average(m => m.Lat);
                var lon = members.Average(m => m.Lon);
                return new ClusterPin(lat, lon, members.Count, members);
            })
            .Take(MaxPins)
            .ToList();

        return clusters;
    }

    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) ComputeBounds(
        IReadOnlyList<ReferenceCaveIndexEntry> entries)
    {
        if (entries.Count == 0)
            return (0, 0, 0, 0);

        var valid = entries.Where(e => e.HasValidCoordinate()).ToList();
        if (valid.Count == 0)
            return (0, 0, 0, 0);

        return (
            valid.Min(e => e.Lat),
            valid.Max(e => e.Lat),
            valid.Min(e => e.Lon),
            valid.Max(e => e.Lon));
    }
}
