namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Canonical English guest / paywall copy for the Windows companion.
/// Keep aligned with website <c>src/utils/guestLibraryCopy.js</c> and
/// <c>docs/guest-library-copy-contract.md</c>.
/// </summary>
public static class GuestLibraryCopy
{
    public const string SignInHeadingPanel = "Sign in with Google to browse caves";
    public const string SignInHeadingMap = "Sign in with Google to unlock the cave map";

    public const string LeadPanel =
        "Catalog pins, Browse, Near me, and community surveys are included with CaveAI Pro - not a guest free library. New Android installs include a 30-day welcome period; after that, an active Google Play subscription keeps access on Android, Windows, and the web.";

    public const string LeadMap =
        "The basemap stays visible. Reference pins, community surveys, and Explore overlays load after you sign in with the Google account linked to CaveAI Pro on Android.";

    public const string FaqAnswer =
        "The home page, Getting started, and region overviews stay public. Reference catalog pins, Browse, Near me, Explore terrain overlays, and community surveys unlock after you sign in with the Google account linked to CaveAI Pro on Android (30-day welcome on first install, then a Google Play subscription).";

    public const string CatalogScale = "76k+";

    /// <summary>Existing Public Library WebView toolbar (Google Sign-In only).</summary>
    public const string ToolbarHint =
        "Sign in with Google to browse caves · same catalog as Android & web";

    public static bool LooksLikeCatalogLock(Exception ex)
    {
        var m = ex.Message ?? string.Empty;
        return m.Contains(SignInHeadingPanel, StringComparison.OrdinalIgnoreCase)
            || m.Contains("CDN lock", StringComparison.OrdinalIgnoreCase)
            || m.Contains("401", StringComparison.OrdinalIgnoreCase)
            || m.Contains("403", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || m.Contains("Forbidden", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Native catalog / compare empty or CDN-lock failure.</summary>
    public static string CatalogLoadFailure(Exception ex) =>
        LooksLikeCatalogLock(ex)
            ? EmptyCatalogStatus
            : $"Failed to load catalog: {ex.Message}\n{LeadPanel}";

    public static string EmptyCatalogStatus => $"{SignInHeadingPanel}\n{LeadPanel}";
}
