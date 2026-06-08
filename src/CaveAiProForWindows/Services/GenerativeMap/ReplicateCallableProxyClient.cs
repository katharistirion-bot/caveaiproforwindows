using System.Text.Json;
using System.Text.Json.Serialization;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// Calls the <c>replicateGenerativeMap</c> Firebase Callable function (server-side Replicate API key).
/// </summary>
public sealed class ReplicateCallableProxyClient
{
    public const string FunctionName = "replicateGenerativeMap";

    private readonly FirebaseCallableClient _callable;
    private readonly Func<FirebaseIdToken?> _tokenProvider;

    public ReplicateCallableProxyClient(
        Func<FirebaseIdToken?>? tokenProvider = null,
        FirebaseCallableClient? callable = null)
    {
        _tokenProvider = tokenProvider ?? (() => FirebaseAuthTokenStore.TryLoad());
        _callable = callable ?? new FirebaseCallableClient(FunctionName);
    }

    public async Task<ReplicateProxyPredictionResult> RunPredictionAsync(
        string modelVersion,
        IReadOnlyDictionary<string, object?> input,
        CancellationToken cancellationToken)
    {
        var token = _tokenProvider()
            ?? throw new InvalidOperationException(
                "Sign in with Google to use cloud AI rendering (Firebase Auth token required).");

        if (!token.IsUsable())
        {
            throw new InvalidOperationException(
                "Your sign-in session expired. Close and reopen the app to sign in again.");
        }

        try
        {
            return await _callable.InvokeAsync<ReplicateProxyPredictionResult>(
                    token,
                    new ReplicateProxyRequest { ModelVersion = modelVersion, Input = input },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FirebaseCallableException ex) when (ex.IsPermissionDenied)
        {
            throw new InvalidOperationException(
                SubscriptionEntitlementResult.AccessDeniedNoSubscriptionMessage,
                ex);
        }
        catch (FirebaseCallableException ex) when (ex.IsUnauthenticated)
        {
            throw new InvalidOperationException(
                "Firebase authentication failed. Sign in again from the startup login screen.",
                ex);
        }
        catch (FirebaseCallableException ex) when (ex.IsRateLimited)
        {
            throw new InvalidOperationException(GenerativeAiRateLimitFormatter.Format(ex), ex);
        }
    }
}

public sealed class ReplicateProxyRequest
{
    public required string ModelVersion { get; init; }

    public required IReadOnlyDictionary<string, object?> Input { get; init; }
}

public sealed class ReplicateProxyPredictionResult
{
    [JsonPropertyName("predictionId")]
    public string? PredictionId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("outputUrl")]
    public string? OutputUrl { get; set; }
}
