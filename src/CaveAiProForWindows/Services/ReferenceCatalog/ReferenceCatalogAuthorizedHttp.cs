using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>
/// GET helper for reference catalog CDN URLs. Attaches Firebase ID Bearer for entitlement-locked
/// Hosting rewrites (<c>referenceCatalogAsset</c>), matching web <c>authorizedDataFetch</c> and
/// Android <c>ReferenceCatalogFetch</c>.
/// </summary>
public static class ReferenceCatalogAuthorizedHttp
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>
    /// Paths rewritten to <c>referenceCatalogAsset</c> (require Bearer + premium).
    /// Meta and shard <c>manifest.json</c> stay public on Hosting.
    /// </summary>
    public static bool IsEntitlementLockedDataUrl(string url)
    {
        try
        {
            var path = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url).AbsolutePath
                : url.Split('?', 2)[0];
            if (string.Equals(path, "/data/reference-shards/manifest.json", StringComparison.Ordinal))
                return false;
            if (string.Equals(path, "/data/reference-caves-catalog.json", StringComparison.Ordinal) ||
                string.Equals(path, "/data/reference-caves-search-index.json", StringComparison.Ordinal) ||
                string.Equals(path, "/data/reference-caves-gr.json", StringComparison.Ordinal) ||
                string.Equals(path, "/data/reference-caves-it.json", StringComparison.Ordinal) ||
                string.Equals(path, "/data/featured-reference-caves.json", StringComparison.Ordinal))
                return true;
            return path.StartsWith("/data/reference-shards/", StringComparison.Ordinal) &&
                   path.EndsWith(".json", StringComparison.Ordinal) &&
                   path.Count(c => c == '/') == 3;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> GetStringAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (IsEntitlementLockedDataUrl(url))
        {
            var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken()
                ?? await CloudPublishWebViewHost.TokenCache.TrySilentRefreshAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (token == null || string.IsNullOrWhiteSpace(token.Raw))
            {
                throw new HttpRequestException(GuestLibraryCopy.SignInHeadingPanel);
            }

            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        }

        using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new HttpRequestException(GuestLibraryCopy.SignInHeadingPanel);
        }
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
