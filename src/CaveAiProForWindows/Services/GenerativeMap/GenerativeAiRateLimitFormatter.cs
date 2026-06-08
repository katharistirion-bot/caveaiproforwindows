namespace CaveAiProForWindows.Services.GenerativeMap;

using CaveAiProForWindows.Services.CloudPublish;

/// <summary>User-facing rate-limit messages for cloud generative AI.</summary>
public static class GenerativeAiRateLimitFormatter
{
    public static string Format(FirebaseCallableException ex)
    {
        var retryMs = ex.RetryAfterMs;
        if (retryMs is > 0)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(retryMs.Value / 60_000.0));
            return minutes >= 60
                ? $"Rate limit reached (20 AI renders per hour). Try again in about {minutes / 60} hour(s)."
                : $"Rate limit reached (20 AI renders per hour). Try again in about {minutes} minute(s).";
        }

        return ex.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)
            ? ex.Message
            : "Rate limit reached (20 AI renders per hour). Please wait and try again.";
    }
}
