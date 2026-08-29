using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>
/// Loads full reference cave detail via country shards (mirrors web <c>referenceCatalogDetailLoader.js</c>).
/// Shard payloads use <see cref="ReferenceCatalogAuthorizedHttp"/> (Bearer); manifest stays public.
/// </summary>
public sealed class ReferenceCatalogDetailLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private Dictionary<string, string>? _countrySlugMap;
    private List<string> _shardSlugs = [];
    private readonly Dictionary<string, Dictionary<string, ReferenceCavePin>> _shardCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ReferenceCavePin?> LoadByIdAsync(
        string id,
        string? countryHint = null,
        CancellationToken cancellationToken = default)
    {
        var needle = id?.Trim();
        if (string.IsNullOrWhiteSpace(needle))
            return null;

        await EnsureManifestAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(countryHint))
        {
            var slug = ResolveSlug(countryHint);
            var fromShard = await TryLoadFromShardAsync(slug, needle, cancellationToken).ConfigureAwait(false);
            if (fromShard != null)
                return fromShard;
        }

        foreach (var slug in _shardSlugs)
        {
            var fromShard = await TryLoadFromShardAsync(slug, needle, cancellationToken).ConfigureAwait(false);
            if (fromShard != null)
                return fromShard;
        }

        return null;
    }

    public ReferenceCavePin ToPin(ReferenceCaveIndexEntry entry) =>
        new()
        {
            Id = entry.Id,
            Name = entry.Name,
            Lat = entry.Lat,
            Lon = entry.Lon,
            Country = entry.Country,
            Region = entry.Region,
            DepthM = entry.DepthM,
            LengthM = entry.LengthM,
            ElevationM = entry.ElevationM,
            CaveType = entry.CaveType,
            OsmType = entry.OsmType,
            OsmId = entry.OsmId,
            Description = entry.Preview,
            Rich = entry.Rich,
        };

    private async Task EnsureManifestAsync(CancellationToken cancellationToken)
    {
        if (_countrySlugMap != null)
            return;

        ReferenceCatalogPaths.EnsureCacheRoot();
        if (File.Exists(ReferenceCatalogPaths.ShardManifestPath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<ReferenceShardManifest>(
                    File.ReadAllText(ReferenceCatalogPaths.ShardManifestPath), JsonOptions);
                if (cached?.Shards.Count > 0)
                {
                    _countrySlugMap = cached.Shards.ToDictionary(
                        s => s.Country,
                        s => s.Slug,
                        StringComparer.OrdinalIgnoreCase);
                    _shardSlugs = cached.Shards.Select(s => s.Slug).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    return;
                }
            }
            catch
            {
                /* refetch */
            }
        }

        try
        {
            var json = await ReferenceCatalogAuthorizedHttp.GetStringAsync(ReferenceCatalogUrls.ShardManifestUrl, cancellationToken)
                .ConfigureAwait(false);
            File.WriteAllText(ReferenceCatalogPaths.ShardManifestPath, json);
            var manifest = JsonSerializer.Deserialize<ReferenceShardManifest>(json, JsonOptions);
            _countrySlugMap = manifest?.Shards.ToDictionary(
                s => s.Country,
                s => s.Slug,
                StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _shardSlugs = manifest?.Shards.Select(s => s.Slug).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? [];
        }
        catch
        {
            _countrySlugMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _shardSlugs = [];
        }
    }

    private string ResolveSlug(string country)
    {
        if (_countrySlugMap != null &&
            _countrySlugMap.TryGetValue(country, out var slug) &&
            !string.IsNullOrWhiteSpace(slug))
            return slug;
        return ReferenceCatalogCountrySlug.Slugify(country);
    }

    private async Task<ReferenceCavePin?> TryLoadFromShardAsync(
        string slug,
        string id,
        CancellationToken cancellationToken)
    {
        if (!_shardCache.TryGetValue(slug, out var map))
        {
            map = await LoadShardMapAsync(slug, cancellationToken).ConfigureAwait(false);
            _shardCache[slug] = map;
        }

        return map.TryGetValue(id, out var pin) ? pin : null;
    }

    private async Task<Dictionary<string, ReferenceCavePin>> LoadShardMapAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        var diskPath = ReferenceCatalogPaths.ShardFilePath(slug);
        string json;
        if (File.Exists(diskPath))
        {
            json = await File.ReadAllTextAsync(diskPath, cancellationToken).ConfigureAwait(false);
            ReferenceCatalogShardCache.RecordAccess(slug);
        }
        else
        {
            try
            {
                json = await ReferenceCatalogAuthorizedHttp.GetStringAsync(ReferenceCatalogUrls.ShardUrl(slug), cancellationToken)
                    .ConfigureAwait(false);
                ReferenceCatalogShardCache.TouchShard(slug, json);
            }
            catch when (File.Exists(diskPath))
            {
                json = await File.ReadAllTextAsync(diskPath, cancellationToken).ConfigureAwait(false);
                ReferenceCatalogShardCache.RecordAccess(slug);
            }
        }

        var shard = JsonSerializer.Deserialize<ReferenceCountryShardFile>(json, JsonOptions);
        var map = new Dictionary<string, ReferenceCavePin>(StringComparer.Ordinal);
        foreach (var raw in shard?.Caves ?? [])
        {
            if (!string.IsNullOrWhiteSpace(raw.Id))
                map[raw.Id] = raw;
        }

        return map;
    }
}
