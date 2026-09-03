using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Silent Firebase ID-token refresh via securetoken.googleapis.com/v1/token.
/// No WebView2 required - uses the stored refresh token.
/// </summary>
internal sealed class FirebaseTokenRefreshClient : IDisposable
{
    private const string RefreshUrl = "https://securetoken.googleapis.com/v1/token?key={0}";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public FirebaseTokenRefreshClient(HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>
    /// Exchanges a refresh token for a new Firebase ID token.
    /// Returns null on any network or parse failure.
    /// </summary>
    public async Task<FirebaseIdToken?> TryRefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var apiKey = FirebaseProjectConfig.ResolveWebApiKey(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_API_KEY"),
            FirebaseProjectConfig.LoadFromEnvironment().WebApiKey,
            FirebaseProjectConfig.ObservedWebApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        try
        {
            var url = string.Format(RefreshUrl, Uri.EscapeDataString(apiKey));
            var body = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("id_token", out var idTokenEl) ||
                idTokenEl.ValueKind != JsonValueKind.String)
                return null;

            var raw = idTokenEl.GetString();
            if (!FirebaseIdTokenParser.TryParse(raw, out var token) || token == null)
                return null;

            return token;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}