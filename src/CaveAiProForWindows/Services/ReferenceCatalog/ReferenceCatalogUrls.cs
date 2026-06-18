using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

public static class ReferenceCatalogUrls
{
    public static string MetaUrl => $"{PublicLibraryCatalog.WebOrigin}/data/reference-catalog-meta.json";
    public static string SearchIndexUrl => $"{PublicLibraryCatalog.WebOrigin}/data/reference-caves-search-index.json";
    public static string SearchIndexFallbackUrl => $"{PublicLibraryCatalog.FirebaseHostingOrigin}/data/reference-caves-search-index.json";
    public static string ShardManifestUrl => $"{PublicLibraryCatalog.WebOrigin}/data/reference-shards/manifest.json";
    public static string ShardUrl(string slug) => $"{PublicLibraryCatalog.WebOrigin}/data/reference-shards/{slug}.json";
    public static string FeaturedCavesUrl => $"{PublicLibraryCatalog.WebOrigin}/data/featured-reference-caves.json";
}
