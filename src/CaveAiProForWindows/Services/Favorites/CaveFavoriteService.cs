using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.Favorites;

public sealed class CaveFavoriteService
{
    private readonly FirebaseRestClient _rest = new();
    private readonly ConcurrentDictionary<string, byte> _local = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string CachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "favorites-cache.json");

    public IReadOnlyCollection<string> CachedFavoriteIds => _local.Keys.ToList();

    public CaveFavoriteService() => LoadCache();

    public bool IsFavoritedLocally(string caveId) =>
        !string.IsNullOrWhiteSpace(caveId) && _local.ContainsKey(caveId);

    public async Task RefreshFromCloudAsync(CancellationToken cancellationToken = default)
    {
        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        var uid = token?.Subject;
        if (token == null || string.IsNullOrWhiteSpace(uid))
            return;

        var favoriteIds = await _rest.ListSubcollectionDocumentIdsAsync(
            token,
            $"users/{uid}/favorites",
            cancellationToken).ConfigureAwait(false);
        var savedCaveIds = await _rest.ListSubcollectionDocumentIdsAsync(
            token,
            $"users/{uid}/saved_caves",
            cancellationToken).ConfigureAwait(false);
        var ids = CaveFavoriteIdMerge.Merge(favoriteIds, savedCaveIds);

        _local.Clear();
        foreach (var id in ids)
            _local[id] = 0;
        SaveCache();
    }

    public async Task SetFavoriteAsync(
        string caveId,
        bool favorited,
        string kind,
        string? caveName,
        string? country,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caveId))
            throw new ArgumentException("Cave id is required.", nameof(caveId));

        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        var uid = token?.Subject;
        if (token == null || string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Sign in to save favorites.");

        var isReference = string.Equals(kind.Trim(), "reference", StringComparison.OrdinalIgnoreCase);
        var collection = isReference ? "saved_caves" : "favorites";
        var docPath = $"users/{uid}/{collection}/{caveId.Trim()}";
        if (favorited)
        {
            var fields = isReference
                ? FirestoreFieldBuilder.BuildFields([
                    new KeyValuePair<string, object?>("caveId", caveId.Trim()),
                    new KeyValuePair<string, object?>("caveName", caveName?.Trim() ?? ""),
                    new KeyValuePair<string, object?>("country", country?.Trim() ?? ""),
                    new KeyValuePair<string, object?>("savedAtMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                ])
                : FirestoreFieldBuilder.BuildFields([
                    new KeyValuePair<string, object?>("caveId", caveId.Trim()),
                    new KeyValuePair<string, object?>("kind", kind.Trim()),
                    new KeyValuePair<string, object?>("caveName", caveName?.Trim() ?? ""),
                    new KeyValuePair<string, object?>("country", country?.Trim() ?? ""),
                    new KeyValuePair<string, object?>("favoritedAtMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
                ]);
            await _rest.PatchDocumentAsync(token, docPath, fields, cancellationToken).ConfigureAwait(false);
            _local[caveId] = 0;
        }
        else
        {
            await _rest.DeleteDocumentAsync(token, docPath, cancellationToken).ConfigureAwait(false);
            _local.TryRemove(caveId, out _);
        }

        SaveCache();
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return;
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(CachePath));
            if (ids == null)
                return;
            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
                _local[id] = 0;
        }
        catch
        {
            // Ignore corrupt cache.
        }
    }

    private void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(_local.Keys.ToList(), JsonOptions));
        }
        catch
        {
            // Best-effort local cache.
        }
    }
}