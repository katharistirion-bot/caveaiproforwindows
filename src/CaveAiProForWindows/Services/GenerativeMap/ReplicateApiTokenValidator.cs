using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Validates a Replicate API token against the public account endpoint (no prediction charges).</summary>
public static class ReplicateApiTokenValidator
{
    public static async Task<bool> ValidateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.replicate.com/v1/account");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

            using var resp = await http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            return resp.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAuthFailure(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}
