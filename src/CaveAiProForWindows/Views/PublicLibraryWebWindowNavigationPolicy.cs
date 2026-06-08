using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Views;

/// <summary>Allowed origins for in-app Public Library / auth WebView2 navigation.</summary>
internal static class PublicLibraryWebWindowNavigationPolicy
{
    public static bool IsAllowed(string uri)
    {
        if (DesktopAuthFallback.IsFallbackUri(uri))
            return true;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme is not ("http" or "https"))
            return false;

        if (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsed.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return true;

        var origin = PublicLibraryCatalog.WebOrigin;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var allowedOrigin)
            && string.Equals(parsed.Host, allowedOrigin.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        if (parsed.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".googleapis.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".gstatic.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
