using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.Gemini;

public static class GeminiProxyClient
{
    public const string DefaultProxyUrl = "https://geminiproxy-6fuo7v7e6q-ew.a.run.app";
    public const string DefaultModel = "gemini-2.5-flash";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static string ProxyUrl =>
        Environment.GetEnvironmentVariable("CAVEAIPRO_GEMINI_PROXY_URL")?.Trim() is { Length: > 0 } url
            ? url
            : DefaultProxyUrl;

    public static bool IsConfigured() => !string.IsNullOrWhiteSpace(ProxyUrl);

    public static JsonObject BuildChatBody(string model, string systemPrompt, string userMessage)
    {
        return new JsonObject
        {
            ["model"] = model,
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } },
            },
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = userMessage } },
                },
            },
        };
    }

    public static async Task<string> PostGenerateContentAsync(JsonObject body, CancellationToken cancellationToken = default)
    {
        if (MicrosoftTestMode.IsActive)
            MicrosoftTestMode.ThrowIfNetworkBlocked("Cloud AI proxy");

        var idToken = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (idToken == null || !idToken.IsUsable())
            throw new HttpRequestException(GuestLibraryCopy.SignInHeadingPanel);

        var appCheck = CloudPublishWebViewHost.AppCheckTokenCache.TryGetUsableToken();
        if (appCheck == null || string.IsNullOrWhiteSpace(appCheck.Raw))
            throw new HttpRequestException("Cloud AI needs App Check. Open Public Library or sign in once, then retry.");

        using var req = new HttpRequestMessage(HttpMethod.Post, ProxyUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken.Raw);
        FirebaseAppCheckHeader.TryApply(req, CloudPublishWebViewHost.AppCheckTokenCache);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var res = await Http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var raw = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Cloud AI rate limit reached. Try again in a few minutes.");
        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException(GuestLibraryCopy.SignInHeadingPanel);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException("Cloud AI HTTP " + (int)res.StatusCode + ": " + Truncate(raw));

        return raw;
    }

    public static string? ExtractTextFromGenerateContentResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("text", out var simplified) && simplified.ValueKind == JsonValueKind.String)
                return simplified.GetString();
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return null;
            var sb = new StringBuilder();
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                    continue;
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString());
                }
            }
            var result = sb.ToString().Trim();
            return result.Length > 0 ? result : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Truncate(string raw) => raw.Length <= 240 ? raw : raw[..240] + "...";
}
