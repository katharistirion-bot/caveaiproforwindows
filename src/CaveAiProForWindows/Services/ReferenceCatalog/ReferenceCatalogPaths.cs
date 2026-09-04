namespace CaveAiProForWindows.Services.ReferenceCatalog;

using System.IO;

public static class ReferenceCatalogPaths
{
    public static string CacheRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "reference-catalog");

    public static string SearchIndexPath => Path.Combine(CacheRoot, "reference-caves-search-index.json");
    public static string MetaPath => Path.Combine(CacheRoot, "reference-catalog-meta.json");
    public static string ShardManifestPath => Path.Combine(CacheRoot, "reference-shards-manifest.json");

    public static string ShardFilePath(string slug) =>
        Path.Combine(CacheRoot, "shards", $"{slug}.json");

    public static string CacheVersionPath => Path.Combine(CacheRoot, ".cache-format-version");

    public static void EnsureCacheRoot() => Directory.CreateDirectory(CacheRoot);
}
