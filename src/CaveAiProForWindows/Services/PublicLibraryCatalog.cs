namespace CaveAiProForWindows.Services;

using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Auth;

/// <summary>
/// Public Cave Library (step 2 browse) — same Firebase catalog as Android; web portal for PC browsing.
/// Keep URL in sync with Android <c>WEB_SITE_ORIGIN</c> and website <c>src/config/site.js</c> (VITE_SITE_ORIGIN).
/// </summary>
public static class PublicLibraryCatalog
{
    /// <summary>Production custom domain — matches website .env.local when DNS is live.</summary>
    private const string DefaultWebOrigin = "https://www.caveaipro.com";

    /// <summary>Firebase Hosting fallback — matches website site.js default when custom domain is not set.</summary>
    public const string FirebaseHostingOrigin = "https://caveaipro-5950e.web.app";

    /// <summary>
    /// Canonical site origin — override at runtime with <c>CAVEAIPRO_WEB_ORIGIN</c> (user or machine env).
    /// </summary>
    public static string WebOrigin
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN");
            return string.IsNullOrWhiteSpace(env) ? DefaultWebOrigin : env.Trim().TrimEnd('/');
        }
    }

    public static string WebMapUrl => WebOrigin + "/map";

    public static string WebExploreMapUrl => WebOrigin + "/map?view=explore";

    public static string WebBrowseUrl => WebMapUrl;

    /// <summary>Appends <c>?embed=windows</c> so the web portal uses redirect sign-in inside WebView2.</summary>
    public static string WithEmbed(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return WithEmbed(WebMapUrl);
        if (url.Contains("embed=", StringComparison.OrdinalIgnoreCase))
            return url;
        var sep = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return url + sep + "embed=windows";
    }

    public static string WebMapUrlEmbedded => WithEmbed(WebMapUrl);

    public static string WebExploreMapUrlEmbedded => WithEmbed(WebExploreMapUrl);

    /// <summary>Explore map URL — restores last viewport when persisted (offline re-open hint).</summary>
    public static string ResolveExploreMapOpenUrl()
    {
        var settings = AppUiSettingsStore.LoadOrDefault().ExploreMap;
        if (settings.PreferLastViewport && !string.IsNullOrWhiteSpace(settings.LastViewportUrl))
            return WithEmbed(settings.LastViewportUrl);
        return WebExploreMapUrlEmbedded;
    }

    /// <summary>Persist Explore map URL (including lat/lon/zoom/layers query) for next Help → Explore map.</summary>
    public static void RememberExploreMapViewportUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.ExploreMap.LastViewportUrl = url.Trim();
        AppUiSettingsStore.Save(all);
    }

    /// <summary>Secure desktop auth endpoint for WebView2 postMessage token delivery.</summary>
    public static string DesktopAuthUrl
    {
        get
        {
            var url = WebMapUrlEmbedded;
            return url.Contains("desktopAuth=", StringComparison.OrdinalIgnoreCase)
                ? url
                : url + "&desktopAuth=v1";
        }
    }

    public static void OpenInBrowser() => OpenMap();

    private static Views.PublicLibraryWebWindow? _activeWindow;

    public static void ShowInAppWindow(System.Windows.Window? owner, string? startUrl = null)
    {
        if (MicrosoftTestMode.IsActive)
        {
            System.Windows.MessageBox.Show(
                owner,
                MicrosoftTestMode.CloudFeatureBlockedMessage,
                "Public Cave Library",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        if (_activeWindow != null)
        {
            try
            {
                if (_activeWindow.IsLoaded)
                {
                    if (_activeWindow.WindowState == System.Windows.WindowState.Minimized)
                        _activeWindow.WindowState = System.Windows.WindowState.Normal;
                    _activeWindow.Show();
                    _activeWindow.Activate();
                    _activeWindow.Focus();
                    return;
                }
            }
            catch
            {
                _activeWindow = null;
            }
        }

        var window = new Views.PublicLibraryWebWindow
        {
            Owner = owner,
            InitialUrl = startUrl ?? WebMapUrlEmbedded,
        };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeWindow, window))
                _activeWindow = null;
        };
        _activeWindow = window;
        window.Show();
        window.Activate();
        window.Focus();
    }

    public static void ShowMapInAppWindow(System.Windows.Window? owner) =>
        ShowInAppWindow(owner, WebMapUrlEmbedded);

    /// <summary>Native reference catalog browse (cached index from caveaipro.com).</summary>
    public static void ShowNativeReferenceCatalog(System.Windows.Window? owner) =>
        Views.ReferenceCatalogWindow.ShowSingleton(owner);

    public static void OpenMap()
    {
        if (MicrosoftTestMode.IsActive)
        {
            System.Windows.MessageBox.Show(
                MicrosoftTestMode.CloudFeatureBlockedMessage,
                "Public Cave Library",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WebMapUrlEmbedded)
        {
            UseShellExecute = true,
        });
    }

    public static string BuildMapCaveUrl(string publishedDocId)
    {
        if (string.IsNullOrWhiteSpace(publishedDocId))
            return WebMapUrl;
        return $"{WebMapUrl}?cave={Uri.EscapeDataString(publishedDocId.Trim())}";
    }

    public static string BuildWorkspaceUrl(string publishedDocId)
    {
        if (string.IsNullOrWhiteSpace(publishedDocId))
            return WebMapUrl;
        return $"{WebOrigin}/workspace/{Uri.EscapeDataString(publishedDocId.Trim())}";
    }

    public static void OpenCaveOnMap(string publishedDocId)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WithEmbed(BuildMapCaveUrl(publishedDocId)))
        {
            UseShellExecute = true,
        });
    }

    public static void OpenCaveInAppWindow(System.Windows.Window? owner, string publishedDocId) =>
        ShowInAppWindow(owner, WithEmbed(BuildMapCaveUrl(publishedDocId)));

    public static void OpenWorkspaceInAppWindow(System.Windows.Window? owner, string publishedDocId) =>
        ShowInAppWindow(owner, WithEmbed(BuildWorkspaceUrl(publishedDocId)));
}
