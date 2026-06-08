using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Firebase Auth Identity Toolkit REST (signInWithIdp) — no JS SDK required.</summary>
internal sealed class FirebaseIdentityToolkitClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public FirebaseIdentityToolkitClient(FirebaseProjectConfig? config = null, HttpMessageHandler? handler = null)
    {
        var cfg = config ?? FirebaseProjectConfig.LoadFromEnvironment();
        _apiKey = cfg.WebApiKey
            ?? throw new InvalidOperationException(DesktopAuthFallback.MissingConfigUserMessage);
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Exchanges a Google OAuth code or id_token for a Firebase ID token.
    /// <paramref name="postBody"/> e.g. <c>code=...&amp;providerId=google.com</c>.
    /// </summary>
    public async Task<FirebaseIdToken> SignInWithIdpAsync(
        string postBody,
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(postBody))
            throw new ArgumentException("postBody is required.", nameof(postBody));
        if (string.IsNullOrWhiteSpace(requestUri))
            throw new ArgumentException("requestUri is required.", nameof(requestUri));

        var url = "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key="
                  + Uri.EscapeDataString(_apiKey);
        var payload = JsonSerializer.Serialize(new
        {
            postBody,
            requestUri,
            returnIdpCredential = true,
            returnSecureToken = true,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"signInWithIdp failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("idToken", out var idEl)
            || idEl.ValueKind != JsonValueKind.String)
            throw new FirebaseRestException("signInWithIdp response missing idToken.", resp.StatusCode, body);

        var raw = idEl.GetString();
        if (!FirebaseIdTokenParser.TryParse(raw, out var token) || token == null)
            throw new FirebaseRestException("signInWithIdp returned an invalid idToken.", resp.StatusCode, body);

        return token;
    }

    private static string Truncate(string? s, int max = 400) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}
