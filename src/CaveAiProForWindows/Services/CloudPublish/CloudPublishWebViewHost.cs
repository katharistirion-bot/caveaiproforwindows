using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Shared WebView2 auth bridge for Push to Cloud — attach once per WebView2 core used for auth / library.
/// </summary>
public static class CloudPublishWebViewHost
{
    private static readonly FirebaseAuthTokenCache SharedTokenCache = new();
    private static readonly object Gate = new();
    private static DesktopAuthWebViewBridge? _bridge;

    public static FirebaseAuthTokenCache TokenCache => SharedTokenCache;

    /// <summary>Idempotent: registers postMessage token delivery on allowed origins.</summary>
    public static DesktopAuthWebViewBridge EnsureAuthBridgeAttached(CoreWebView2 core)
    {
        lock (Gate)
        {
            _bridge ??= new DesktopAuthWebViewBridge(SharedTokenCache);
            _bridge.Attach(core);
            return _bridge;
        }
    }

    public static void DetachAuthBridge()
    {
        lock (Gate)
        {
            _bridge?.Detach();
        }
    }
}
