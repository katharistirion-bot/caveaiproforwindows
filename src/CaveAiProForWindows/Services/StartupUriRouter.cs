using System.IO;

namespace CaveAiProForWindows.Services;

public sealed class StartupIntent
{
    public IReadOnlyList<string> SurveyFilePaths { get; init; } = Array.Empty<string>();
    public string? ExploreMapUrl { get; init; }
}

public static class StartupUriRouter
{
    public static StartupIntent Parse(string[] args)
    {
        var files = new List<string>();
        string? exploreUrl = null;

        foreach (var raw in args ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var arg = raw.Trim().Trim('"');

            if (arg.StartsWith("caveaipro://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host ?? "";
                    if (host.Equals("explore", StringComparison.OrdinalIgnoreCase) ||
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

        return new StartupIntent { SurveyFilePaths = files, ExploreMapUrl = exploreUrl };
    }

    private static string NormalizeExploreUrl(string raw, Uri uri)
    {
        if (raw.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
            return raw;

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return PublicLibraryCatalog.WebMapUrlEmbedded;

        return PublicLibraryCatalog.WebMapUrlEmbedded.Contains('?')
            ? $"{PublicLibraryCatalog.WebMapUrlEmbedded}&{query}"
            : $"{PublicLibraryCatalog.WebMapUrlEmbedded}?{query}";
    }
}