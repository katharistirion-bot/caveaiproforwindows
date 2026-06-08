using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Bundled auth.html fallback when the web desktopAuth endpoint is unavailable.</summary>
internal static class DesktopAuthFallback
{
    /// <summary>
    /// Virtual host for bundled auth.html. Must match a Firebase Authentication authorized domain
    /// (localhost is included by default on new Firebase projects).
    /// </summary>
    private const string VirtualHost = "localhost";

    /// <summary>Tracks cores that already received the document-created injection script.</summary>
    private static readonly HashSet<CoreWebView2> RegisteredCores = new(ReferenceEqualityComparer.Instance);

    public static void TryRegisterVirtualHost(CoreWebView2 core)
    {
        var folder = ResolveAuthFolder();
        if (string.IsNullOrEmpty(folder))
            return;

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            folder,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    public static bool IsFallbackUri(string? uri) =>
        !string.IsNullOrWhiteSpace(uri)
        && Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && string.Equals(u.Host, VirtualHost, StringComparison.OrdinalIgnoreCase);

    public static string FallbackUri => $"https://{VirtualHost}/auth.html";

    /// <summary>
    /// Registers a script that runs before any page JS on every new document — required for bundled auth.html.
    /// Call once after <c>EnsureCoreWebView2Async</c> and before the first <c>Navigate</c>.
    /// </summary>
    public static async Task EnsureFirebaseConfigScriptRegisteredAsync(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);
        lock (RegisteredCores)
        {
            if (!RegisteredCores.Add(core))
                return;
        }

        var cfg = FirebaseProjectConfig.LoadFromEnvironment().ToWebClientConfig();
        var json = JsonSerializer.Serialize(cfg.ToFirebaseInitializeAppObject());
        var script = BuildDocumentCreatedScript(json, cfg.IsUsable);

        await core.AddScriptToExecuteOnDocumentCreatedAsync(script).ConfigureAwait(true);
    }

    /// <summary>Navigate helper — registers config script then opens bundled auth.html.</summary>
    public static async Task PrepareFallbackNavigationAsync(CoreWebView2 core)
    {
        await EnsureFirebaseConfigScriptRegisteredAsync(core).ConfigureAwait(true);
    }

    /// <summary>
    /// Legacy name — prefer <see cref="EnsureFirebaseConfigScriptRegisteredAsync"/> before navigation.
    /// Re-registering on an already-loaded page does not re-run document-created scripts; triggers reload.
    /// </summary>
    public static async Task InjectFirebaseConfigAsync(CoreWebView2 core)
    {
        await EnsureFirebaseConfigScriptRegisteredAsync(core).ConfigureAwait(true);
        if (IsFallbackUri(core.Source))
            core.Reload();
    }

    public static bool HasUsableFirebaseConfig() =>
        FirebaseProjectConfig.LoadFromEnvironment().ToWebClientConfig().IsUsable;

    public static string MissingConfigUserMessage =>
        "Firebase is not configured for offline sign-in.\n\n" +
        "Set user or machine environment variable CAVEAIPRO_FIREBASE_API_KEY " +
        "(or fill Assets/DesktopAuth/firebase-config.json next to the app) then restart.";

    private static string BuildDocumentCreatedScript(string firebaseConfigJson, bool isUsable)
    {
        // Runs before HTML parsers execute — sets config for auth.html and no-ops on other origins.
        return $$"""
            (function () {
              if (window.__CAVEAI_FIREBASE_CONFIG_INJECTED__) return;
              window.__CAVEAI_FIREBASE_CONFIG_INJECTED__ = true;
              var host = (location && location.hostname) || '';
              if (host !== '{{VirtualHost}}') return;
              window.__CAVEAI_FIREBASE_CONFIG__ = {{firebaseConfigJson}};
              window.__CAVEAI_FIREBASE_CONFIG_READY__ = {{(isUsable ? "true" : "false")}};
              try {
                window.dispatchEvent(new CustomEvent('caveai-firebase-config', {
                  detail: window.__CAVEAI_FIREBASE_CONFIG__
                }));
              } catch (e) {}
            })();
            """;
    }

    private static string? ResolveAuthFolder()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", "DesktopAuth");
        return Directory.Exists(candidate) ? candidate : null;
    }
}
