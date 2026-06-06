using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Allowed origins for in-app Public Library / auth WebView2 navigation.</summary>
internal static class PublicLibraryWebWindowNavigationPolicy
{
    public static bool IsAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme is not ("http" or "https"))
            return false;

        var origin = PublicLibraryCatalog.WebOrigin;
        if (Uri.TryCreate(origin, UriKind.Absolute, out var allowedOrigin)
            && string.Equals(parsed.Host, allowedOrigin.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        if (parsed.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
