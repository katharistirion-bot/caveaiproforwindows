using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.SurfaceMap;

public static class SurfaceMapLayerPrefsSync
{
    public static string MergeLayerParamsIntoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        var s = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var query = ParseQuery(uri.Query);
        query["layers"] = EncodeLayers(s);
        var builder = new UriBuilder(uri) { Query = BuildQuery(query) };
        return builder.Uri.ToString();
    }

    public static void SyncSurfaceMapFromExploreUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !url.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("layers", out var layersRaw) || string.IsNullOrWhiteSpace(layersRaw))
            return;

        var parsed = DecodeLayers(layersRaw);
        if (parsed.Count == 0)
            return;

        var all = AppUiSettingsStore.LoadOrDefault();
        var surface = all.SurfaceMap;
        var changed = false;

        if (parsed.TryGetValue("hillshade", out var hillshade) && surface.HillshadeEnabled != hillshade)
        {
            surface.HillshadeEnabled = hillshade;
            changed = true;
        }

        if (parsed.TryGetValue("copernicus", out var copernicus) && surface.CopernicusDsmEnabled != copernicus)
        {
            surface.CopernicusDsmEnabled = copernicus;
            changed = true;
        }

        if (!changed)
            return;

        AppUiSettingsStore.Save(all);
    }

    internal static string EncodeLayers(SurfaceMapPersistedState s)
    {
        var parts = new List<string>(2)
        {
            s.HillshadeEnabled ? "hillshade=1" : "hillshade=0",
            s.CopernicusDsmEnabled ? "copernicus=1" : "copernicus=0",
        };
        return string.Join(",", parts);
    }

    internal static Dictionary<string, bool> DecodeLayers(string raw)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = part[..eq].Trim();
            var val = part[(eq + 1)..].Trim();
            if (val is "1" or "true")
                result[key] = true;
            else if (val is "0" or "false")
                result[key] = false;
        }

        return result;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
            return result;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = "";
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static string BuildQuery(Dictionary<string, string> query)
    {
        if (query.Count == 0)
            return "";

        return string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={(kv.Value != null ? Uri.EscapeDataString(kv.Value) : "")}"));
    }
}
