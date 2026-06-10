using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.Auth;

/// <summary>Clears persisted Firebase auth and optionally signs out the web session in WebView2.</summary>
public static class FirebaseAuthSession
{
    internal const string FirebaseSignOutScript =
        """
        try {
          if (window.firebase && window.firebase.auth) window.firebase.auth().signOut();
        } catch (e) {}
        """;

    /// <summary>Email from the cached token, if any.</summary>
    public static string? CurrentAccountEmail =>
        CloudPublishWebViewHost.TokenCache.Current?.Email
        ?? CloudPublishWebViewHost.TokenCache.TryGetUsableToken()?.Email;

    /// <summary>Clears in-memory and persisted Firebase ID token.</summary>
    public static void SignOutLocal()
    {
        CloudPublishWebViewHost.TokenCache.Clear();
        CloudPublishWebViewHost.DetachAuthBridge();
        AccountSessionState.Clear();
    }

    /// <summary>Best-effort Firebase web sign-out in an active WebView2 core.</summary>
    public static async Task TrySignOutWebViewAsync(CoreWebView2? core, bool reload = true)
    {
        if (core == null)
            return;

        try
        {
            await core.ExecuteScriptAsync(FirebaseSignOutScript).ConfigureAwait(true);
            if (reload)
                core.Reload();
        }
        catch
        {
            /* navigation race — ignore */
        }
    }

    /// <summary>Clears local token storage and signs out any supplied WebView session.</summary>
    public static async Task SignOutAsync(CoreWebView2? webView = null)
    {
        SignOutLocal();
        await TrySignOutWebViewAsync(webView).ConfigureAwait(true);
    }
}
