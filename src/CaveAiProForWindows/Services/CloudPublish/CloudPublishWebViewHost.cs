using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Shared WebView2 auth bridge for Push to Cloud — attach once per WebView2 core used for auth / library.
/// </summary>
public static class CloudPublishWebViewHost
{
    private static readonly FirebaseAuthTokenCache SharedTokenCache = CreateSharedTokenCache();
    private static readonly object Gate = new();
    private static readonly Dictionary<CoreWebView2, DesktopAuthWebViewBridge> Bridges =
        new(ReferenceEqualityComparer.Instance);

    public static FirebaseAuthTokenCache TokenCache => SharedTokenCache;

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
        ArgumentNullException.ThrowIfNull(core);

        lock (Gate)
        {
            if (!Bridges.TryGetValue(core, out var bridge))
            {
                bridge = new DesktopAuthWebViewBridge(SharedTokenCache);
                Bridges.Add(core, bridge);
            }

            bridge.Attach(core);
            return bridge;
        }
    }

    public static void DetachAuthBridge()
    {
        lock (Gate)
        {
            foreach (var bridge in Bridges.Values)
                bridge.Detach();
            Bridges.Clear();
        }
    }
}
