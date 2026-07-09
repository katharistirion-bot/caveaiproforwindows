using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// postMessage contract between the web auth page (<c>embed=windows&amp;desktopAuth=v1</c>)
/// and the desktop WebView2 bridge.
/// </summary>
public static class DesktopAuthProtocol
{
    public const string TokenMessageType = "caveai-desktop-auth-token";
    public const string AppCheckTokenMessageType = "caveai-desktop-appcheck-token";
    public const string ReadyMessageType = "caveai-desktop-auth-ready";
    public const string RequestMessageType = "caveai-desktop-auth-request";
    public const string AppCheckRequestMessageType = "caveai-desktop-appcheck-request";
    public const string RedirectFallbackMessageType = "caveai-auth-redirect-fallback";
    public const string AuthErrorMessageType = "caveai-auth-error";
    public const string AuthConsoleMessageType = "caveai-auth-console";

    public static bool TryParseAuthConsoleMessage(string? json, out string? level, out string? message)
    {
        level = null;
        message = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String
                || !string.Equals(typeEl.GetString(), AuthConsoleMessageType, StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("message", out var msgEl)
                || msgEl.ValueKind != JsonValueKind.String)
                return false;

            level = root.TryGetProperty("level", out var levelEl) && levelEl.ValueKind == JsonValueKind.String
                ? levelEl.GetString()
                : "log";
            message = msgEl.GetString();
            return !string.IsNullOrWhiteSpace(message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseAuthErrorMessage(string? json, out string? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String
                || !string.Equals(typeEl.GetString(), AuthErrorMessageType, StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("message", out var msgEl)
                || msgEl.ValueKind != JsonValueKind.String)
                return false;

            message = msgEl.GetString();
            return !string.IsNullOrWhiteSpace(message);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseRedirectFallbackMessage(string? json, out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String
                || !string.Equals(typeEl.GetString(), RedirectFallbackMessageType, StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("url", out var urlEl)
                || urlEl.ValueKind != JsonValueKind.String)
                return false;

            url = urlEl.GetString();
            return !string.IsNullOrWhiteSpace(url);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Parses a WebView2 <see cref="Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs"/> payload.</summary>
    public static bool TryParseTokenMessage(string? json, out FirebaseIdToken? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
                return false;

            if (!string.Equals(typeEl.GetString(), TokenMessageType, StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("idToken", out var tokenEl)
                || tokenEl.ValueKind != JsonValueKind.String)
                return false;

            var raw = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (!FirebaseIdTokenParser.TryParse(raw, out var parsed) || parsed == null)
                return false;

            token = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string BuildRequestMessageJson() =>
        JsonSerializer.Serialize(new { type = RequestMessageType });

    public static string BuildAppCheckRequestMessageJson() =>
        JsonSerializer.Serialize(new { type = AppCheckRequestMessageType });

    /// <summary>Parses App Check JWT from WebView2 postMessage.</summary>
    public static bool TryParseAppCheckTokenMessage(string? json, out FirebaseAppCheckToken? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
                return false;

            if (!string.Equals(typeEl.GetString(), AppCheckTokenMessageType, StringComparison.Ordinal))
                return false;

            if (!root.TryGetProperty("appCheckToken", out var tokenEl)
                || tokenEl.ValueKind != JsonValueKind.String)
                return false;

            var raw = tokenEl.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (!FirebaseAppCheckTokenParser.TryParse(raw, out var parsed) || parsed == null)
                return false;

            token = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
