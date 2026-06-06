namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Parses Public Cave Library URLs for Firestore <c>published_caves</c> document ids.</summary>
public static class PublishedCaveUrlParser
{
    public static bool TryExtractDocId(string? url, out string docId)
    {
        docId = "";
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (TryReadQueryParam(uri, "cave", out docId))
            return true;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2
            && string.Equals(segments[0], "workspace", StringComparison.OrdinalIgnoreCase))
        {
            docId = Uri.UnescapeDataString(segments[1]).Trim();
            return docId.Length > 0;
        }

        return false;
    }

    private static bool TryReadQueryParam(Uri uri, string key, out string value)
    {
        value = "";
        var query = uri.Query;
        if (string.IsNullOrEmpty(query))
            return false;
        if (query.StartsWith('?'))
            query = query[1..];

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var name = Uri.UnescapeDataString(part[..eq]);
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                continue;
            value = Uri.UnescapeDataString(part[(eq + 1)..]).Trim();
            return value.Length > 0;
        }

        return false;
    }
}
