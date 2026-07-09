using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Shared WebView2 auth bridge for Push to Cloud — attach once per WebView2 core used for auth / library.
/// </summary>
public static class CloudPublishWebViewHost
{
    private static readonly FirebaseAuthTokenCache SharedTokenCache = CreateSharedTokenCache();
    private static readonly FirebaseAppCheckTokenCache SharedAppCheckTokenCache = new();
    private static readonly object Gate = new();
    private static DesktopAuthWebViewBridge? _bridge;

    public static FirebaseAuthTokenCache TokenCache => SharedTokenCache;

    public static FirebaseAppCheckTokenCache AppCheckTokenCache => SharedAppCheckTokenCache;

    private static FirebaseAuthTokenCache CreateSharedTokenCache()
    {
        var cache = new FirebaseAuthTokenCache();
        var saved = FirebaseAuthTokenStore.TryLoad();
        if (saved != null)
            cache.Update(saved);
        return cache;
    }

    /// <summary>Idempotent: registers postMessage token delivery on allowed origins.</summary>
    public static DesktopAuthWebViewBridge EnsureAuthBridgeAttached(CoreWebView2 core)
    {
        lock (Gate)
        {
            _bridge ??= new DesktopAuthWebViewBridge(SharedTokenCache, SharedAppCheckTokenCache);
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
