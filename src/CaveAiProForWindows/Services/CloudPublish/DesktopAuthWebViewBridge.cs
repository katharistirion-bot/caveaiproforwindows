using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// WebView2 bridge: injects the desktop auth script and receives Firebase ID tokens via postMessage.
/// </summary>
public sealed class DesktopAuthWebViewBridge : IDisposable
{
    private const string IdentityToolkitSignInWithIdpFilter =
        "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp*";

    private readonly FirebaseAuthTokenCache _cache;
    private readonly DesktopAuthOAuthExchangeGate _oauthExchangeGate = new();
    private FirebaseIdentityToolkitClient? _identityToolkit;
    private CoreWebView2? _core;
    private bool _identityToolkitFilterRegistered;
    private bool _documentCreatedScriptRegistered;

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
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.WebResourceResponseReceived += OnWebResourceResponseReceived;

        if (!_identityToolkitFilterRegistered)
        {
            core.AddWebResourceRequestedFilter(
                IdentityToolkitSignInWithIdpFilter,
                CoreWebView2WebResourceContext.All);
            _identityToolkitFilterRegistered = true;
        }

        if (!_documentCreatedScriptRegistered)
        {
            _ = core.AddScriptToExecuteOnDocumentCreatedAsync(DesktopAuthBridgeScript.JavaScript);
            _documentCreatedScriptRegistered = true;
        }
    }

    public void Detach()
    {
        if (_core == null)
            return;

        _core.WebMessageReceived -= OnWebMessageReceived;
        _core.NavigationStarting -= OnNavigationStarting;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
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

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Uri))
            return;

        FirebaseProjectConfig.TryObserveWebApiKeyFromUri(e.Uri);

        if (!DesktopAuthHandlerCallback.TryParse(e.Uri, out var callback) || callback == null)
            return;

        // Exchange OAuth callback here — do not load handler HTML (avoids code reuse / race / retry loops).
        e.Cancel = true;
        _ = ExchangeHandlerCallbackAsync(callback);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _core == null)
            return;

        if (!IsAllowedOrigin(_core.Source))
            return;

        await InjectBridgeScriptAsync().ConfigureAwait(true);

        if (DesktopAuthFallback.IsFallbackUri(_core.Source))
            await RunAuthCompletionWithRetriesAsync().ConfigureAwait(true);

        await RequestTokenDeliveryAsync().ConfigureAwait(true);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.WriteLine("[DesktopAuthBridge] WebMessageReceived: empty payload.");
                return;
            }

            if (DesktopAuthProtocol.TryParseRedirectFallbackMessage(json, out var redirectUrl)
                && !string.IsNullOrWhiteSpace(redirectUrl))
            {
                if (DesktopAuthFlowInvariants.IsOAuthCallbackUri(redirectUrl))
                {
                    App.WriteStartupLog(
                        "DesktopAuthBridge: blocked navigate to OAuth callback URL (would break sign-in loop)");
                    return;
                }

                if (!PublicLibraryWebWindowNavigationPolicy.IsAllowed(redirectUrl))
                {
                    App.WriteStartupLog("DesktopAuthBridge: blocked redirect fallback to disallowed URL");
                    return;
                }

                Debug.WriteLine("[DesktopAuthBridge] Redirect fallback navigate: " + redirectUrl);
                App.WriteStartupLog("DesktopAuthBridge: redirect fallback navigate");
                _core?.Navigate(redirectUrl);
                return;
            }

            if (DesktopAuthProtocol.TryParseAuthErrorMessage(json, out var authError)
                && !string.IsNullOrWhiteSpace(authError))
            {
                Debug.WriteLine("[DesktopAuthBridge] Auth page error: " + authError);
                App.WriteStartupLog("DesktopAuthBridge: auth page error — " + authError);
                return;
            }

            if (DesktopAuthProtocol.TryParseAuthConsoleMessage(json, out var level, out var consoleMsg)
                && !string.IsNullOrWhiteSpace(consoleMsg))
            {
                Debug.WriteLine("[AuthWebView console/" + (level ?? "log") + "] " + consoleMsg);
#if DEBUG
                App.WriteStartupLog("[AuthWebView console/" + (level ?? "log") + "] " + consoleMsg);
#endif
                return;
            }

            if (!DesktopAuthProtocol.TryParseTokenMessage(json, out var token) || token == null)
            {
                var preview = json.Length > 120 ? json[..120] + "…" : json;
                Debug.WriteLine("[DesktopAuthBridge] WebMessageReceived: non-token message: " + preview);
                return;
            }

            Debug.WriteLine(
                $"[DesktopAuthBridge] WebMessageReceived: Firebase token accepted — sub={token.Subject ?? "?"} " +
                $"email={token.Email ?? "?"} exp={token.ExpiresAtUtc:O} (JWT length={token.Raw.Length}).");
            App.WriteStartupLog(
                "DesktopAuthBridge: token received for " + (token.Email ?? token.Subject ?? "?"));

            _cache.Update(token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[DesktopAuthBridge] WebMessageReceived handler error: " + ex.Message);
        }
    }

    private async Task ExchangeHandlerCallbackAsync(DesktopAuthHandlerCallback callback)
    {
        if (!_oauthExchangeGate.TryBeginExchange(callback.DedupeKey))
            return;

        try
        {
            _identityToolkit ??= new FirebaseIdentityToolkitClient();
            var token = await _identityToolkit
                .SignInWithIdpAsync(callback.PostBody, DesktopAuthRedirectUrls.AuthHandlerRequestUri)
                .ConfigureAwait(true);

            AcceptToken(token, "REST token received for ");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[DesktopAuthBridge] REST signInWithIdp failed: " + ex.Message);
            App.WriteStartupLog("DesktopAuthBridge: REST exchange failed — " + ex.Message);
        }
        finally
        {
            _oauthExchangeGate.EndExchange();
            NavigateToFallbackAfterExchange();
        }
    }

    private void NavigateToFallbackAfterExchange()
    {
        if (_core == null || DesktopAuthFallback.IsFallbackUri(_core.Source))
            return;

        if (_cache.TryGetUsableToken() != null)
            return;

        // Keep the live website auth page when bundled firebase-config.json is still a build placeholder.
        if (!DesktopAuthFallback.HasUsableFirebaseConfig())
            return;

        _ = NavigateBundledAuthAfterExchangeAsync();
    }

    private async Task NavigateBundledAuthAfterExchangeAsync()
    {
        if (_core == null)
            return;

        try
        {
            await DesktopAuthFallback.PrepareFallbackNavigationAsync(_core).ConfigureAwait(true);
            _core.Navigate(DesktopAuthFlowInvariants.PostExchangeNavigationUri);
            await DesktopAuthFallback.PushFirebaseConfigToPageAsync(_core).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[DesktopAuthBridge] Bundled auth navigation failed: " + ex.Message);
        }
    }

    private async void OnWebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var requestUri = e.Request.Uri;
        if (!requestUri.Contains("signInWithIdp", StringComparison.OrdinalIgnoreCase))
            return;

        if (e.Response.StatusCode is < 200 or >= 300)
            return;

        try
        {
            using var contentStream = await e.Response.GetContentAsync().ConfigureAwait(true);
            using var reader = new StreamReader(contentStream);
            var body = await reader.ReadToEndAsync().ConfigureAwait(true);
            if (TryParseIdTokenFromSignInWithIdpResponse(body, out var token) && token != null)
                AcceptToken(token, "Network token received for ");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[DesktopAuthBridge] signInWithIdp response capture failed: " + ex.Message);
        }
    }

    private static bool TryParseIdTokenFromSignInWithIdpResponse(string body, out FirebaseIdToken? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(body))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("idToken", out var idEl)
                || idEl.ValueKind != JsonValueKind.String)
                return false;

            var raw = idEl.GetString();
            return FirebaseIdTokenParser.TryParse(raw, out token) && token != null;
        }
        catch
        {
            return false;
        }
    }

    private void AcceptToken(FirebaseIdToken token, string logPrefix)
    {
        if (_cache.TryGetUsableToken()?.Raw == token.Raw)
            return;

        Debug.WriteLine(
            $"[DesktopAuthBridge] {logPrefix.TrimEnd()} — email={token.Email ?? "?"} sub={token.Subject ?? "?"}");
        App.WriteStartupLog(logPrefix + (token.Email ?? token.Subject ?? "?"));
        _cache.Update(token);
    }

    private async Task RunAuthCompletionWithRetriesAsync()
    {
        if (_core == null)
            return;

        var delaysMs = new[] { 0, 400, 900, 1800, 3200 };
        foreach (var delay in delaysMs)
        {
            if (delay > 0)
                await Task.Delay(delay).ConfigureAwait(true);

            if (_core == null || !DesktopAuthFallback.IsFallbackUri(_core.Source))
                return;

            if (_cache.TryGetUsableToken() != null)
                return;

            try
            {
                var outcome = await _core.ExecuteScriptAsync(DesktopAuthCompletionScript.JavaScript)
                    .ConfigureAwait(true);
                var normalized = NormalizeScriptOutcome(outcome);
                Debug.WriteLine("[DesktopAuthBridge] Auth completion script: " + normalized);
                LogAuthCompletionOutcome(normalized);

                if (normalized.Contains("token-delivered", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DesktopAuthBridge] Auth completion script failed: " + ex.Message);
                App.WriteStartupLog("DesktopAuthBridge: completion script failed — " + ex.Message);
            }
        }
    }

    private static string NormalizeScriptOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
            return "null";
        return outcome.Trim().Trim('"');
    }

    private static void LogAuthCompletionOutcome(string outcome)
    {
        if (outcome.Contains("token-delivered", StringComparison.OrdinalIgnoreCase))
            App.WriteStartupLog("DesktopAuthBridge: token delivered after redirect");
        else
            App.WriteStartupLog("DesktopAuthBridge: auth completion — " + outcome);
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

        if (DesktopAuthFallback.IsFallbackUri(uriString))
            return true;

        return uri.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".gstatic.com", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => Detach();
}
