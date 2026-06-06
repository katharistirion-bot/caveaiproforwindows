using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// postMessage contract between the web auth page (<c>embed=windows&amp;desktopAuth=v1</c>)
/// and the desktop WebView2 bridge.
/// </summary>
public static class DesktopAuthProtocol
{
    public const string TokenMessageType = "caveai-desktop-auth-token";
    public const string ReadyMessageType = "caveai-desktop-auth-ready";
    public const string RequestMessageType = "caveai-desktop-auth-request";

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
}
