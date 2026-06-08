namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Parsed OAuth callback on Firebase <c>/__/auth/handler</c> (Google code or id_token).</summary>
public sealed class DesktopAuthHandlerCallback
{
    public required string PostBody { get; init; }

    public required string DedupeKey { get; init; }

    public static bool TryParse(string? uriString, out DesktopAuthHandlerCallback? callback)
    {
        callback = null;
        if (string.IsNullOrWhiteSpace(uriString))
            return false;
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            return false;
        if (!uri.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!uri.AbsolutePath.Contains("/__/auth/handler", StringComparison.OrdinalIgnoreCase))
            return false;

        if (TryParseQueryParam(uri.Query, "code", out var code) && !string.IsNullOrWhiteSpace(code))
        {
            callback = new DesktopAuthHandlerCallback
            {
                PostBody = "code=" + Uri.EscapeDataString(code) + "&providerId=google.com",
                DedupeKey = "code:" + code,
            };
            return true;
        }

        var fragment = uri.Fragment;
        if (fragment.Length > 1
            && TryParseFragmentParam(fragment, "id_token", out var idToken)
            && !string.IsNullOrWhiteSpace(idToken))
        {
            callback = new DesktopAuthHandlerCallback
            {
                PostBody = "id_token=" + idToken + "&providerId=google.com",
                DedupeKey = "id_token:" + idToken[..Math.Min(32, idToken.Length)],
            };
            return true;
        }

        return false;
    }

    private static bool TryParseQueryParam(string query, string name, out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(query))
            return false;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = Uri.UnescapeDataString(part[..eq]);
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = Uri.UnescapeDataString(part[(eq + 1)..]);
            return true;
        }

        return false;
    }

    private static bool TryParseFragmentParam(string fragment, string name, out string? value)
    {
        value = null;
        var trimmed = fragment.StartsWith('#') ? fragment[1..] : fragment;
        return TryParseQueryParam("?" + trimmed, name, out value);
    }
}
