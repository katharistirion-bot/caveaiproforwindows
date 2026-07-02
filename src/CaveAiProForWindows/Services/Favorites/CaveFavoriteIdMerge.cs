namespace CaveAiProForWindows.Services.Favorites;

/// <summary>Merges community favorites and reference saved caves (web parity: useUserFavorites.js).</summary>
internal static class CaveFavoriteIdMerge
{
    public static HashSet<string> Merge(IEnumerable<string> favoriteIds, IEnumerable<string> savedCaveIds)
    {
        var merged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in favoriteIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                merged.Add(id);
        }

        foreach (var id in savedCaveIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                merged.Add(id);
        }

        return merged;
    }
}