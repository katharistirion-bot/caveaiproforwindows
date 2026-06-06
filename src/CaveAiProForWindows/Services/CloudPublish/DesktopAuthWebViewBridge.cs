using CaveAiProForWindows.Services;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// WebView2 bridge: injects the desktop auth script and receives Firebase ID tokens via postMessage.
/// </summary>
public sealed class DesktopAuthWebViewBridge : IDisposable
{
    private readonly FirebaseAuthTokenCache _cache;
    private CoreWebView2? _core;

    public DesktopAuthWebViewBridge(FirebaseAuthTokenCache cache)
    {
        _cache = cache;
    }

    public FirebaseAuthTokenCache Cache => _cache;

    /// <summary>Idempotent attach after <c>EnsureCoreWebView2Async</c>.</summary>
    public void Attach(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (ReferenceEquals(_core, core))
            return;

        Detach();
        _core = core;
        core.Settings.IsWebMessageEnabled = true;
        core.WebMessageReceived += OnWebMessageReceived;
        core.NavigationCompleted += OnNavigationCompleted;
    }

    public void Detach()
    {
        if (_core == null)
            return;

        _core.WebMessageReceived -= OnWebMessageReceived;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core = null;
    }

    /// <summary>Ask the page (or injected bridge) to deliver the current Firebase ID token.</summary>
    public async Task RequestTokenDeliveryAsync()
    {
        if (_core == null)
            return;

        try
        {
            var json = DesktopAuthProtocol.BuildRequestMessageJson();
            await _core.ExecuteScriptAsync(
                    $"window.postMessage({json}, '*'); if (window.caveAiDesktopAuth && window.__CAVEAI_DESKTOP_ID_TOKEN__) window.caveAiDesktopAuth.deliverToken(window.__CAVEAI_DESKTOP_ID_TOKEN__);")
                .ConfigureAwait(true);
        }
        catch
        {
            /* best-effort nudge */
        }
    }

    public async Task InjectBridgeScriptAsync()
    {
        if (_core == null)
            return;

        try
        {
            await _core.ExecuteScriptAsync(DesktopAuthBridgeScript.JavaScript).ConfigureAwait(true);
        }
        catch
        {
            /* navigation race — ignore */
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _core == null)
            return;

        if (!IsAllowedOrigin(_core.Source))
            return;

        await InjectBridgeScriptAsync().ConfigureAwait(true);
        await RequestTokenDeliveryAsync().ConfigureAwait(true);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (!DesktopAuthProtocol.TryParseTokenMessage(json, out var token) || token == null)
                return;

            _cache.Update(token);
        }
        catch
        {
            /* never break navigation */
        }
    }

    private static bool IsAllowedOrigin(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
            return false;
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;

        var origin = PublicLibraryCatalog.WebOrigin;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var allowed)
            && string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        return uri.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Detach();
}
