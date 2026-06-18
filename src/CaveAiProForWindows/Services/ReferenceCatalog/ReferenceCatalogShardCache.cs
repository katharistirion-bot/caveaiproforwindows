using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Keeps the last N country shard JSON files on disk under reference-catalog/shards.</summary>
public static class ReferenceCatalogShardCache
{
    public const int MaxCachedShards = 3;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static void TouchShard(string slug, string json)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return;

        ReferenceCatalogPaths.EnsureCacheRoot();
        var shardDir = Path.Combine(ReferenceCatalogPaths.CacheRoot, "shards");
        Directory.CreateDirectory(shardDir);

        var path = ReferenceCatalogPaths.ShardFilePath(slug);
        File.WriteAllText(path, json);

        var accessPath = Path.Combine(shardDir, $"{slug}.access.json");
        var record = new ShardAccessRecord { Slug = slug, LastAccessUtc = DateTimeOffset.UtcNow };
        File.WriteAllText(accessPath, JsonSerializer.Serialize(record, JsonOptions));

        PruneToMax(MaxCachedShards);
    }

    public static void RecordAccess(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return;

        var shardDir = Path.Combine(ReferenceCatalogPaths.CacheRoot, "shards");
        if (!Directory.Exists(shardDir))
            return;

        var accessPath = Path.Combine(shardDir, $"{slug}.access.json");
        var record = new ShardAccessRecord { Slug = slug, LastAccessUtc = DateTimeOffset.UtcNow };
        File.WriteAllText(accessPath, JsonSerializer.Serialize(record, JsonOptions));
        PruneToMax(MaxCachedShards);
    }

    public static void PruneToMax(int maxShards)
    {
        var shardDir = Path.Combine(ReferenceCatalogPaths.CacheRoot, "shards");
        if (!Directory.Exists(shardDir))
            return;

        var shards = Directory.EnumerateFiles(shardDir, "*.json")
            .Where(f => !f.EndsWith(".access.json", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (shards.Count <= maxShards)
            return;

        var accessTimes = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in shards)
        {
            var accessPath = Path.Combine(shardDir, $"{slug}.access.json");
            if (File.Exists(accessPath))
            {
                try
                {
                    var rec = JsonSerializer.Deserialize<ShardAccessRecord>(File.ReadAllText(accessPath), JsonOptions);
                    if (rec != null)
                    {
                        accessTimes[slug] = rec.LastAccessUtc;
                        continue;
                    }
                }
                catch
                {
                    /* fall through */
                }
            }

            accessTimes[slug] = File.GetLastWriteTimeUtc(ReferenceCatalogPaths.ShardFilePath(slug!));
        }

        foreach (var slug in accessTimes.OrderBy(kv => kv.Value).Select(kv => kv.Key).Take(shards.Count - maxShards))
        {
            try
            {
                File.Delete(ReferenceCatalogPaths.ShardFilePath(slug));
                var accessPath = Path.Combine(shardDir, $"{slug}.access.json");
                if (File.Exists(accessPath))
                    File.Delete(accessPath);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    private sealed class ShardAccessRecord
    {
        public string Slug { get; set; } = "";
        public DateTimeOffset LastAccessUtc { get; set; }
    }
}
