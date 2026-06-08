namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Builds Firebase Auth handler URLs for Google sign-in redirect in WebView2.</summary>
internal static class DesktopAuthRedirectUrls
{
    public static string BuildGoogleSignInRedirectUrl(string continueUrl)
    {
        if (string.IsNullOrWhiteSpace(continueUrl))
            throw new ArgumentException("Continue URL is required.", nameof(continueUrl));

        var cfg = FirebaseProjectConfig.LoadFromEnvironment().ToWebClientConfig();
        if (!cfg.IsUsable)
            throw new InvalidOperationException(DesktopAuthFallback.MissingConfigUserMessage);

        var authDomain = string.IsNullOrWhiteSpace(cfg.AuthDomain)
            ? $"{cfg.ProjectId}.firebaseapp.com"
            : cfg.AuthDomain.Trim();

        var continueEncoded = Uri.EscapeDataString(continueUrl.Trim());
        var apiKey = Uri.EscapeDataString(cfg.ApiKey);

        return "https://" + authDomain + "/__/auth/handler"
               + "?apiKey=" + apiKey
               + "&appName=" + Uri.EscapeDataString("[DEFAULT]")
               + "&authType=signInViaRedirect"
               + "&redirectUrl=" + continueEncoded
               + "&providerId=google.com"
               + "&v=10.14.1";
    }

    /// <summary>OAuth redirect URI registered with Google (Firebase auth handler).</summary>
    public static string AuthHandlerRequestUri
    {
        get
        {
            var cfg = FirebaseProjectConfig.LoadFromEnvironment().ToWebClientConfig();
            var authDomain = string.IsNullOrWhiteSpace(cfg.AuthDomain)
                ? $"{cfg.ProjectId}.firebaseapp.com"
                : cfg.AuthDomain.Trim();
            return "https://" + authDomain + "/__/auth/handler";
        }
    }
}
