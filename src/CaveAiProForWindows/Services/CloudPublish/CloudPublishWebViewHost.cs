using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Shared WebView2 auth bridge for Push to Cloud — attach once per WebView2 core used for Public Library.
/// </summary>
public static class CloudPublishWebViewHost
{
    private static readonly FirebaseAuthTokenCache SharedTokenCache = new();
    private static readonly object Gate = new();
    private static FirebaseWebAuthTokenSniffer? _sniffer;

    public static FirebaseAuthTokenCache TokenCache => SharedTokenCache;

    /// <summary>Idempotent: registers passive token sniffing on Firebase/Google API requests.</summary>
    public static void EnsureAuthSnifferAttached(CoreWebView2 core)
    {
        lock (Gate)
        {
            _sniffer ??= new FirebaseWebAuthTokenSniffer(SharedTokenCache);
            _sniffer.Attach(core);
        }
    }

    public static void DetachAuthSniffer()
    {
        lock (Gate)
        {
            _sniffer?.Detach();
        }
    }
}
