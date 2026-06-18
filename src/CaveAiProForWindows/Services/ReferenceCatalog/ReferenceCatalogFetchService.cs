using System.IO;
using System.Net.Http;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>
/// Fetches and caches the reference catalog search index (mirrors Android <c>ReferenceCatalogFetch.kt</c> stale detection).
/// </summary>
public sealed class ReferenceCatalogFetchService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static ReferenceCatalogBrowseState? _memoryCache;

    public async Task<ReferenceCatalogBrowseState> LoadBrowseIndexAsync(
        bool forceRefresh = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (forceRefresh)
            _memoryCache = null;

        if (!forceRefresh && _memoryCache != null && _memoryCache.IndexEntries.Count > 0)
            return _memoryCache;

        ReferenceCatalogPaths.EnsureCacheRoot();

        ReferenceIndexFile? disk = null;
        var stale = false;
        if (!forceRefresh && TryLoadDiskIndex(out disk, out stale) && !stale)
        {
            progress?.Report($"Reference catalog — {disk!.Entries.Count:N0} caves (cached)");
            _memoryCache = ToState(disk, "disk");
            return _memoryCache;
        }

        if (stale)
            progress?.Report("Reference catalog index is stale — refreshing…");

        var remoteMeta = await TryFetchMetaAsync(cancellationToken).ConfigureAwait(false);
        foreach (var url in new[] { ReferenceCatalogUrls.SearchIndexUrl, ReferenceCatalogUrls.SearchIndexFallbackUrl })
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Downloading reference catalog index…");
            var index = await TryDownloadIndexAsync(url, cancellationToken).ConfigureAwait(false);
            if (index == null || index.Entries.Count == 0)
                continue;

            if (remoteMeta != null)
                File.WriteAllText(ReferenceCatalogPaths.MetaPath, JsonSerializer.Serialize(remoteMeta, JsonOptions));

            SaveIndex(index);
            _memoryCache = ToState(index, "network", fromNetwork: true);
            progress?.Report($"Reference catalog — {index.Entries.Count:N0} caves");
            return _memoryCache;
        }

        if (disk != null && disk.Entries.Count > 0)
        {
            progress?.Report($"Offline — using cached index ({disk.Entries.Count:N0} caves)");
            _memoryCache = ToState(disk, "disk-stale", isOffline: true);
            return _memoryCache;
        }

        return new ReferenceCatalogBrowseState { IsOffline = true };
    }

    /// <summary>Loads index from disk only (no network) — for X-RAY reference pin overlay.</summary>
    public static IReadOnlyList<ReferenceCaveIndexEntry> TryLoadCachedIndexEntries()
    {
        if (!File.Exists(ReferenceCatalogPaths.SearchIndexPath))
            return [];

        try
        {
            var json = File.ReadAllText(ReferenceCatalogPaths.SearchIndexPath);
            var disk = JsonSerializer.Deserialize<ReferenceIndexFile>(json, JsonOptions);
            return disk?.Entries.Where(e => e.HasValidCoordinate()).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ReferenceCaveIndexEntry>> LoadFeaturedCavesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await Http.GetStringAsync(ReferenceCatalogUrls.FeaturedCavesUrl, cancellationToken)
                .ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<FeaturedReferenceCavesFile>(json, JsonOptions);
            return file?.Caves?.Where(e => e.HasValidCoordinate()).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private bool TryLoadDiskIndex(out ReferenceIndexFile? disk, out bool stale)
    {
        disk = null;
        stale = false;
        if (!File.Exists(ReferenceCatalogPaths.SearchIndexPath))
            return false;

        try
        {
            var json = File.ReadAllText(ReferenceCatalogPaths.SearchIndexPath);
            disk = JsonSerializer.Deserialize<ReferenceIndexFile>(json, JsonOptions);
            if (disk == null || disk.Entries.Count == 0)
                return false;

            var remoteMeta = TryLoadCachedMeta();
            if (remoteMeta != null)
                stale = IsDiskIndexStale(disk, remoteMeta);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ReferenceCatalogMeta? TryLoadCachedMeta()
    {
        if (!File.Exists(ReferenceCatalogPaths.MetaPath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ReferenceCatalogMeta>(
                File.ReadAllText(ReferenceCatalogPaths.MetaPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ReferenceCatalogMeta?> TryFetchMetaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = ReferenceCatalogUrls.MetaUrl + "?v=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ReferenceCatalogMeta>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ReferenceIndexFile?> TryDownloadIndexAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var sep = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var fetchUrl = url + sep + "v=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var json = await Http.GetStringAsync(fetchUrl, cancellationToken).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize<ReferenceIndexFile>(json, JsonOptions);
            return index?.Entries.Count > 0 ? index : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveIndex(ReferenceIndexFile index)
    {
        ReferenceCatalogPaths.EnsureCacheRoot();
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = false });
        File.WriteAllText(ReferenceCatalogPaths.SearchIndexPath, json);
    }

    internal static bool IsDiskIndexStale(ReferenceIndexFile disk, ReferenceCatalogMeta meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.UpdatedAt) &&
            !string.IsNullOrWhiteSpace(disk.UpdatedAt) &&
            !string.Equals(meta.UpdatedAt, disk.UpdatedAt, StringComparison.Ordinal))
            return true;

        if (meta.CaveCount > 0 && disk.Entries.Count < (int)(meta.CaveCount * 0.9))
            return true;

        return false;
    }

    private static ReferenceCatalogBrowseState ToState(
        ReferenceIndexFile index,
        string source,
        bool fromNetwork = false,
        bool isOffline = false) =>
        new()
        {
            IndexEntries = index.Entries.Where(e => e.HasValidCoordinate()).ToList(),
            Attribution = index.Attribution,
            Source = source,
            FromNetwork = fromNetwork,
            IsOffline = isOffline && !fromNetwork,
        };
}
