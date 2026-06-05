using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Minimal Replicate REST client for create + poll prediction workflow.</summary>
public sealed class ReplicateApiClient
{
    private static readonly Uri ApiRoot = new("https://api.replicate.com/v1/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly Func<string?> _tokenProvider;

    public ReplicateApiClient(Func<string?> tokenProvider, HttpClient? http = null)
    {
        _tokenProvider = tokenProvider;
        _http = http ?? new HttpClient { BaseAddress = ApiRoot, Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<ReplicatePrediction> CreatePredictionAsync(
        string modelVersion,
        IReadOnlyDictionary<string, object?> input,
        CancellationToken cancellationToken)
    {
        var token = RequireToken();
        using var req = new HttpRequestMessage(HttpMethod.Post, "predictions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { version = modelVersion, input }, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Replicate create prediction failed ({(int)resp.StatusCode}): {TrimBody(body)}");

        var prediction = JsonSerializer.Deserialize<ReplicatePrediction>(body, JsonOptions)
            ?? throw new InvalidOperationException("Replicate returned an empty prediction payload.");
        return prediction;
    }

    public async Task<ReplicatePrediction> GetPredictionAsync(string id, CancellationToken cancellationToken)
    {
        var token = RequireToken();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"predictions/{id}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Replicate get prediction failed ({(int)resp.StatusCode}): {TrimBody(body)}");

        return JsonSerializer.Deserialize<ReplicatePrediction>(body, JsonOptions)
            ?? throw new InvalidOperationException("Replicate returned an empty prediction payload.");
    }

    public async Task<ReplicatePrediction> WaitForPredictionAsync(
        string predictionId,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null)
    {
        pollInterval ??= TimeSpan.FromSeconds(2);
        timeout ??= TimeSpan.FromMinutes(8);
        var deadline = DateTimeOffset.UtcNow + timeout.Value;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException("Replicate prediction timed out.");

            var prediction = await GetPredictionAsync(predictionId, cancellationToken).ConfigureAwait(false);
            switch (prediction.Status?.ToLowerInvariant())
            {
                case "succeeded":
                    return prediction;
                case "failed":
                case "canceled":
                    throw new InvalidOperationException(
                        prediction.Error ?? $"Replicate prediction {prediction.Status}.");
                default:
                    progress?.Report($"Replicate: {prediction.Status ?? "processing"}…");
                    await Task.Delay(pollInterval.Value, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    public static async Task<byte[]> DownloadBytesAsync(string url, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        return await http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public static string? TryGetFirstOutputUrl(ReplicatePrediction prediction)
    {
        if (prediction.Output is not { } output)
            return null;

        if (output.ValueKind == JsonValueKind.String)
            return output.GetString();

        if (output.ValueKind == JsonValueKind.Array &&
            output.GetArrayLength() > 0 &&
            output[0].ValueKind == JsonValueKind.String)
            return output[0].GetString();

        return null;
    }

    private string RequireToken()
    {
        var token = _tokenProvider();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                "No Replicate API token configured. Add one under LEGAL & SETTINGS → Generative map (Replicate), or set CAVEAIPRO_REPLICATE_API_TOKEN.");
        return token.Trim();
    }

    private static string TrimBody(string body) =>
        body.Length <= 400 ? body : body[..400] + "…";
}

public sealed class ReplicatePrediction
{
    public string? Id { get; set; }

    public string? Status { get; set; }

    public string? Error { get; set; }

    public JsonElement? Output { get; set; }
}
