using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Minimal HTTP client for Firebase Callable Cloud Functions (no Firebase SDK on desktop).
/// </summary>
public sealed class FirebaseCallableClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly FirebaseProjectConfig _config;
    private readonly string _functionName;
    private readonly string _region;

    public FirebaseCallableClient(
        string functionName,
        FirebaseProjectConfig? config = null,
        string region = "us-central1",
        HttpClient? http = null)
    {
        _functionName = functionName;
        _region = region;
        _config = config ?? FirebaseProjectConfig.LoadFromEnvironment();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public string EndpointUrl =>
        $"https://{_region}-{_config.ProjectId}.cloudfunctions.net/{_functionName}";

    public async Task<TResponse> InvokeAsync<TResponse>(
        FirebaseIdToken idToken,
        object payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idToken);
        if (!idToken.IsUsable())
            throw new InvalidOperationException("Firebase ID token is missing or expired. Sign in again.");

        using var req = new HttpRequestMessage(HttpMethod.Post, EndpointUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken.Raw);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new CallableEnvelope(payload), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        CallableResponseEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CallableResponseEnvelope>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Callable function returned invalid JSON ({(int)resp.StatusCode}): {Truncate(body)}",
                ex);
        }

        if (envelope?.Error != null)
            throw new FirebaseCallableException(envelope.Error);

        if (envelope?.Result == null)
        {
            throw new InvalidOperationException(
                $"Callable function returned no result ({(int)resp.StatusCode}): {Truncate(body)}");
        }

        if (typeof(TResponse) == typeof(JsonElement))
            return (TResponse)(object)envelope.Result.Value;

        var typed = envelope.Result.Value.Deserialize<TResponse>(JsonOptions);
        return typed ?? throw new InvalidOperationException("Callable function returned an empty result payload.");
    }

    private static string Truncate(string? s, int max = 400) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    private sealed class CallableEnvelope(object data)
    {
        public object Data { get; } = data;
    }

    private sealed class CallableResponseEnvelope
    {
        public JsonElement? Result { get; set; }

        public CallableError? Error { get; set; }
    }
}

public sealed class CallableError
{
    public string? Message { get; set; }

    public string? Status { get; set; }

    public JsonElement? Details { get; set; }
}

public sealed class FirebaseCallableException : InvalidOperationException
{
    public CallableError Error { get; }

    public FirebaseCallableException(CallableError error)
        : base(error.Message ?? "Cloud function call failed.")
    {
        Error = error;
    }

    public bool IsUnauthenticated =>
        string.Equals(Error.Status, "UNAUTHENTICATED", StringComparison.OrdinalIgnoreCase);

    public bool IsPermissionDenied =>
        string.Equals(Error.Status, "PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase);

    public bool IsRateLimited =>
        string.Equals(Error.Status, "RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Milliseconds until the current rate-limit window resets (when provided by the cloud function).</summary>
    public long? RetryAfterMs
    {
        get
        {
            if (Error.Details is not { ValueKind: JsonValueKind.Object } details)
                return null;
            if (details.TryGetProperty("retryAfterMs", out var prop) && prop.TryGetInt64(out var ms) && ms > 0)
                return ms;
            return null;
        }
    }
}
