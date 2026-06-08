namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Non-negotiable rules for WebView2 + Firebase Google sign-in on desktop.
/// Regression tests assert these — do not change without updating tests.
/// </summary>
public static class DesktopAuthFlowInvariants
{
    /// <summary>Bundled auth.html flow identifier (host redirect + REST exchange on OAuth callback).</summary>
    public const string BundledAuthFlowVersion = "host-redirect-v2";

    /// <summary>Patterns that must never reappear in bundled auth.html (they broke login repeatedly).</summary>
    public static readonly string[] ForbiddenBundledAuthHtmlPatterns =
    [
        "signInWithRedirect(",
        "scheduleHostRedirectFallback",
    ];

    /// <summary>Required sign-in mechanism in bundled auth.html.</summary>
    public const string RequiredHostRedirectMessageType = "caveai-auth-redirect-fallback";

    /// <summary>
    /// OAuth callbacks (<c>/__/auth/handler?code=</c>) must be exchanged via REST — never load handler HTML
    /// and never navigate back to the same callback URL on failure (causes infinite retry loops).
    /// </summary>
    public static bool IsOAuthCallbackUri(string? uri) =>
        DesktopAuthHandlerCallback.TryParse(uri, out _);

    /// <summary>After REST exchange (success or failure), always return user to bundled auth page — never handler callback.</summary>
    public static string PostExchangeNavigationUri => DesktopAuthFallback.FallbackUri;

    /// <summary>Validates bundled auth.html still follows the host-redirect-only contract.</summary>
    public static IReadOnlyList<string> ValidateBundledAuthHtml(string html)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(html))
        {
            errors.Add("auth.html is empty.");
            return errors;
        }

        foreach (var forbidden in ForbiddenBundledAuthHtmlPatterns)
        {
            if (html.Contains(forbidden, StringComparison.Ordinal))
                errors.Add("auth.html must not contain forbidden pattern: " + forbidden);
        }

        if (!html.Contains(RequiredHostRedirectMessageType, StringComparison.Ordinal))
            errors.Add("auth.html must post host redirect via " + RequiredHostRedirectMessageType);

        if (!html.Contains(BundledAuthFlowVersion, StringComparison.Ordinal))
            errors.Add("auth.html must declare flow version " + BundledAuthFlowVersion);

        return errors;
    }
}
