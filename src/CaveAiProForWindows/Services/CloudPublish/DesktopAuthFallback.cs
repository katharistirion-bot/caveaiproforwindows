using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Bundled auth.html fallback when the web desktopAuth endpoint is unavailable.</summary>
internal static class DesktopAuthFallback
{
    private const string VirtualHost = "caveai-auth.local";

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

    public static async Task PrepareFallbackNavigationAsync(CoreWebView2 core)
    {
        var cfg = FirebaseProjectConfig.LoadFromEnvironment();
        if (string.IsNullOrWhiteSpace(cfg.WebApiKey))
            return;

        var json = JsonSerializer.Serialize(new
        {
            apiKey = cfg.WebApiKey,
            authDomain = $"{cfg.ProjectId}.firebaseapp.com",
            projectId = cfg.ProjectId,
            storageBucket = cfg.StorageBucket,
        });

        await core.AddScriptToExecuteOnDocumentCreatedAsync(
                $"window.__CAVEAI_FIREBASE_CONFIG__ = {json};")
            .ConfigureAwait(true);
    }

    public static async Task InjectFirebaseConfigAsync(CoreWebView2 core)
    {
        await PrepareFallbackNavigationAsync(core).ConfigureAwait(true);
    }

    private static string? ResolveAuthFolder()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "Assets", "DesktopAuth");
        return Directory.Exists(candidate) ? candidate : null;
    }
}
