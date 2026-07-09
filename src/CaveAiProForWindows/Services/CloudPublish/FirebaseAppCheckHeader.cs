using System.Net.Http;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Attaches X-Firebase-AppCheck when a usable token is cached (no-op until enforce).</summary>
internal static class FirebaseAppCheckHeader
{
    public const string HeaderName = "X-Firebase-AppCheck";

    public static void TryApply(HttpRequestMessage request, FirebaseAppCheckTokenCache? cache)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = cache?.TryGetUsableToken()?.Raw;
        if (string.IsNullOrWhiteSpace(token))
            return;

        request.Headers.TryAddWithoutValidation(HeaderName, token);
    }
}