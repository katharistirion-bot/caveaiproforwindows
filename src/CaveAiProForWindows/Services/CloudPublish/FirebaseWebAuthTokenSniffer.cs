using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Passively captures <c>Authorization: Bearer</c> Firebase ID tokens from outbound WebView2 requests
/// to Google Firebase / Firestore / Storage endpoints while the user browses the Public Library (zero website changes).
/// </summary>
public sealed class FirebaseWebAuthTokenSniffer : IDisposable
{
    private static readonly string[] SniffHostSuffixes =
    [
        "firestore.googleapis.com",
        "firebasestorage.googleapis.com",
        "storage.googleapis.com",
        "identitytoolkit.googleapis.com",
        "securetoken.googleapis.com",
        "www.googleapis.com",
    ];

    private readonly FirebaseAuthTokenCache _cache;
    private CoreWebView2? _core;
    private bool _filtersRegistered;

    public FirebaseWebAuthTokenSniffer(FirebaseAuthTokenCache cache)
    {
        _cache = cache;
    }

    public FirebaseAuthTokenCache Cache => _cache;

    /// <summary>Attach to a live <see cref="CoreWebView2"/> (call after <c>EnsureCoreWebView2Async</c>).</summary>
    public void Attach(CoreWebView2 core)
    {
        Detach();
        _core = core;
        RegisterFilters(core);
        core.WebResourceRequested += OnWebResourceRequested;
    }

    public void Detach()
    {
        if (_core == null)
            return;
        _core.WebResourceRequested -= OnWebResourceRequested;
        _core = null;
        _filtersRegistered = false;
    }

    private void RegisterFilters(CoreWebView2 core)
    {
        if (_filtersRegistered)
            return;
        foreach (var suffix in SniffHostSuffixes)
            core.AddWebResourceRequestedFilter($"https://{suffix}/*", CoreWebView2WebResourceContext.All);
        _filtersRegistered = true;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var req = e.Request;
            if (string.IsNullOrWhiteSpace(req.Uri))
                return;
            if (!Uri.TryCreate(req.Uri, UriKind.Absolute, out var uri))
                return;
            if (!ShouldSniffHost(uri.Host))
                return;

            if (!TryReadBearerToken(req, out var bearer))
                return;

            if (!FirebaseIdTokenParser.TryParse(bearer, out var parsed) || parsed == null)
                return;

            _cache.Update(parsed);
        }
        catch
        {
            /* passive sniffer — never break web navigation */
        }
    }

    private static bool ShouldSniffHost(string host)
    {
        foreach (var suffix in SniffHostSuffixes)
        {
            if (string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryReadBearerToken(CoreWebView2WebResourceRequest request, out string token)
    {
        token = "";
        if (!request.Headers.Contains("Authorization"))
            return false;
        var auth = request.Headers.GetHeader("Authorization");
        if (string.IsNullOrWhiteSpace(auth))
            return false;
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        token = auth[prefix.Length..].Trim();
        return token.Length > 0;
    }

    public void Dispose() => Detach();
}
