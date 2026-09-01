using System.IO;

namespace CaveAiProForWindows.Services;

public sealed class StartupIntent
{
    public IReadOnlyList<string> SurveyFilePaths { get; init; } = Array.Empty<string>();
    public string? ExploreMapUrl { get; init; }
    /// <summary>Field-trip share URL (<c>view=fieldtrip</c>, <c>tripId=</c>, or <c>ft=</c> pack).</summary>
    public string? FieldTripShareUrl { get; init; }
}

public static class StartupUriRouter
{
    public static StartupIntent Parse(string[] args)
    {
        var files = new List<string>();
        string? exploreUrl = null;
        string? fieldTripUrl = null;

        foreach (var raw in args ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var arg = raw.Trim().Trim('"');

            if (arg.StartsWith("caveaipro://", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("https://www.caveaipro.com/", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("https://caveaipro.com/", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("http://www.caveaipro.com/", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("http://caveaipro.com/", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host ?? "";
                    var query = uri.Query ?? "";
                    var isFieldTrip =
                        arg.Contains("view=fieldtrip", StringComparison.OrdinalIgnoreCase)
                        || query.Contains("tripId=", StringComparison.OrdinalIgnoreCase)
                        || query.Contains("ft=", StringComparison.OrdinalIgnoreCase)
                        || host.Equals("fieldtrip", StringComparison.OrdinalIgnoreCase);

                    if (isFieldTrip)
                    {
                        fieldTripUrl = NormalizeFieldTripUrl(arg, uri);
                    }
                    else if (IsLibraryMapDeepLink(host, arg))
                    {
                        exploreUrl = PublicLibraryCatalog.EnsureWatchDefaultZoom(NormalizeLibraryMapUrl(arg, uri));
                    }
                    else if (host.Equals("open", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(path))
                            files.Add(Path.GetFullPath(path));
                    }
                }
                continue;
            }

            if (!File.Exists(arg)) continue;
            if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                files.Add(Path.GetFullPath(arg));
        }

        return new StartupIntent
        {
            SurveyFilePaths = files,
            ExploreMapUrl = exploreUrl,
            FieldTripShareUrl = fieldTripUrl,
        };
    }

    internal static bool IsLibraryMapDeepLink(string host, string raw)
    {
        if (host.Equals("explore", StringComparison.OrdinalIgnoreCase)
            || host.Equals("watch", StringComparison.OrdinalIgnoreCase)
            || host.Equals("nearme", StringComparison.OrdinalIgnoreCase))
            return true;
        return raw.Contains("view=explore", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("view=watch", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("view=nearme", StringComparison.OrdinalIgnoreCase);
    }

    private static string MapViewFromLink(string host, string raw)
    {
        if (host.Equals("watch", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("view=watch", StringComparison.OrdinalIgnoreCase))
            return "watch";
        if (host.Equals("nearme", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("view=nearme", StringComparison.OrdinalIgnoreCase))
            return "nearme";
        return "explore";
    }

    /// <summary>
    /// HTTPS share URLs keep their query (including <c>view=watch</c> / <c>view=nearme</c>).
    /// Protocol links <c>caveaipro://explore|watch|nearme?…</c> become caveaipro.com/map URLs.
    /// </summary>
    private static string NormalizeLibraryMapUrl(string raw, Uri uri)
    {
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && IsLibraryMapDeepLink(uri.Host, raw))
            return raw;

        var query = uri.Query.TrimStart('?');
        if (!string.IsNullOrWhiteSpace(query)
            && query.Contains("view=", StringComparison.OrdinalIgnoreCase))
            return $"{PublicLibraryCatalog.WebOrigin}/map?{query}";

        var view = MapViewFromLink(uri.Host, raw);
        var baseUrl = $"{PublicLibraryCatalog.WebOrigin}/map?view={view}";
        if (string.IsNullOrWhiteSpace(query))
            return baseUrl;
        return $"{baseUrl}&{query}";
    }

    private static string NormalizeFieldTripUrl(string raw, Uri uri)
    {
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return raw;

        // caveaipro://fieldtrip?... → https share URL
        var query = uri.Query.TrimStart('?');
        var baseUrl = $"{FieldTrip.FieldTripShareCodec.SiteOrigin.TrimEnd('/')}/map?view=fieldtrip";
        if (string.IsNullOrWhiteSpace(query))
            return baseUrl;
        return $"{baseUrl}&{query}";
    }
}
