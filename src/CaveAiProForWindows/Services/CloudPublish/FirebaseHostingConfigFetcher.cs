using System.Net.Http;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Fetches the public Firebase Web API key from Hosting init.json when build-time inject is missing.</summary>
internal static class FirebaseHostingConfigFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _cachedApiKey;

    public static async Task<string?> TryFetchWebApiKeyAsync(CancellationToken cancellationToken = default)
    {
        if (FirebaseProjectConfig.IsUsableApiKey(_cachedApiKey))
            return _cachedApiKey;

        var cfg = FirebaseProjectConfig.LoadFromEnvironment();
        var hosts = new[]
        {
            cfg.AuthDomain,
            $"{FirebaseProjectConfig.DefaultProjectId}.firebaseapp.com",
            "caveaipro-5950e.web.app",
        };

        foreach (var host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(host))
                continue;

            var key = await TryFetchFromHostAsync(host, cancellationToken).ConfigureAwait(false);
            if (!FirebaseProjectConfig.IsUsableApiKey(key))
                continue;

            _cachedApiKey = key!.Trim();
            FirebaseProjectConfig.SetObservedWebApiKey(_cachedApiKey);
            return _cachedApiKey;
        }

        return null;
    }

    private static async Task<string?> TryFetchFromHostAsync(string host, CancellationToken cancellationToken)
    {
        var url = $"https://{host.Trim().TrimEnd('/')}/__/firebase/init.json";
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("apiKey", out var apiEl))
                return null;

            return apiEl.ValueKind == JsonValueKind.String ? apiEl.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
