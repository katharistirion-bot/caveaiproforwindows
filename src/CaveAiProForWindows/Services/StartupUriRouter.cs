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
                    else if (host.Equals("explore", StringComparison.OrdinalIgnoreCase) ||
                        arg.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
                    {
                        exploreUrl = NormalizeExploreUrl(arg, uri);
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

    private static string NormalizeExploreUrl(string raw, Uri uri)
    {
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && raw.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
            return raw;

        if (raw.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
            return raw;

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return PublicLibraryCatalog.WebMapUrlEmbedded;

        return PublicLibraryCatalog.WebMapUrlEmbedded.Contains('?')
            ? $"{PublicLibraryCatalog.WebMapUrlEmbedded}&{query}"
            : $"{PublicLibraryCatalog.WebMapUrlEmbedded}?{query}";
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
