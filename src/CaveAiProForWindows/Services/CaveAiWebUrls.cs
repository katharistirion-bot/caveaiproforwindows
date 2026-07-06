using System.Diagnostics;
using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Services;

public static class CaveAiWebUrls
{
    private const int DescMax = 320;

    public static string BaseUrl => $"{PublicLibraryCatalog.WebOrigin}/ai";

    public static bool IsCaveAiWebDeepLink(string? uri)
    {
        if (!Uri.TryCreate(uri?.Trim(), UriKind.Absolute, out var u))
            return false;
        if (u.Scheme is not ("http" or "https"))
            return false;

        var host = u.Host.Trim().ToLowerInvariant();
        if (host is not ("www.caveaipro.com" or "caveaipro.com"
                or "caveaipro-5950e.web.app" or "caveaipro-5950e.firebaseapp.com"))
            return false;

        var path = u.AbsolutePath.TrimEnd('/');
        return path.Equals("/ai", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildFromReference(ReferenceCaveIndexEntry entry, ReferenceCavePin? detail = null)
    {
        var q = new List<KeyValuePair<string, string>>
        {
            new("refId", entry.Id.Trim()),
            new("name", entry.Name.Trim()),
        };
        AppendIfPresent(q, "country", entry.Country);
        AppendIfPresent(q, "region", entry.Region);
        AppendRounded(q, "depth", entry.DepthM);
        AppendRounded(q, "length", entry.LengthM);
        AppendIfPresent(q, "access", detail?.AccessNote);
        var desc = detail != null
            ? ReferenceCatalogDisplay.PinDescription(detail)
            : ReferenceCatalogDisplay.PreviewText(entry);
        AppendIfPresent(q, "desc", TruncateDesc(desc));
        AppendCoord(q, "lat", entry.Lat);
        AppendCoord(q, "lon", entry.Lon);
        return BuildUrl(q);
    }

    public static string BuildFromSurveyLink(ReferenceSurveyLink link) =>
        BuildFromReference(new ReferenceCaveIndexEntry
        {
            Id = link.Id,
            Name = link.Name,
            Country = link.Country,
            Lat = link.Lat,
            Lon = link.Lon,
        });

    public static string BuildFromPublished(
        string caveId,
        string caveName,
        string? country = null,
        string? region = null,
        double? depthM = null,
        double? lengthM = null,
        string? descriptionSnippet = null,
        bool hasSurvey = false,
        double? lat = null,
        double? lon = null)
    {
        var q = new List<KeyValuePair<string, string>>
        {
            new("caveId", caveId.Trim()),
            new("name", caveName.Trim()),
        };
        AppendIfPresent(q, "country", country);
        AppendIfPresent(q, "region", region);
        AppendRounded(q, "depth", depthM);
        AppendRounded(q, "length", lengthM);
        AppendIfPresent(q, "desc", TruncateDesc(descriptionSnippet));
        if (hasSurvey)
            q.Add(new("survey", "1"));
        if (lat is { } latVal)
            AppendCoord(q, "lat", latVal);
        if (lon is { } lonVal)
            AppendCoord(q, "lon", lonVal);
        return BuildUrl(q);
    }

    public static void OpenInDefaultBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string BuildUrl(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var sb = new StringBuilder(BaseUrl);
        sb.Append('?');
        var first = true;
        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!first)
                sb.Append('&');
            first = false;
            sb.Append(Uri.EscapeDataString(key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(value.Trim()));
        }
        return sb.ToString();
    }

    private static void AppendIfPresent(List<KeyValuePair<string, string>> q, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            q.Add(new(key, value.Trim()));
    }

    private static void AppendRounded(List<KeyValuePair<string, string>> q, string key, double? metres)
    {
        if (metres is > 0 and var m)
            q.Add(new(key, Math.Round(m).ToString(CultureInfo.InvariantCulture)));
    }

    private static void AppendCoord(List<KeyValuePair<string, string>> q, string key, double value)
    {
        if (double.IsFinite(value))
            q.Add(new(key, value.ToString("G", CultureInfo.InvariantCulture)));
    }

    private static string TruncateDesc(string? text)
    {
        var parts = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var t = string.Join(' ', parts).Trim();
        if (t.Length <= DescMax)
            return t;
        return t[..(DescMax - 1)] + "...";
    }
}