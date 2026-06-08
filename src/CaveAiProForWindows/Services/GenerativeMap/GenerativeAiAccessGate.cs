using System.Windows;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Secrets;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>
/// Gates generative AI: cloud proxy requires Firebase Auth; optional BYOK when
/// <c>CAVEAIPRO_REPLICATE_BYOK=1</c> and a local Replicate token is configured.
/// </summary>
public static class GenerativeAiAccessGate
{
    public static bool UseDirectByok =>
        string.Equals(Environment.GetEnvironmentVariable("CAVEAIPRO_REPLICATE_BYOK"), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable("CAVEAIPRO_REPLICATE_BYOK"), "true", StringComparison.OrdinalIgnoreCase);

    public static bool IsCloudProxyMode => !UseDirectByok;

    public static bool IsConfigured()
    {
        if (UseDirectByok)
            return ReplicateApiTokenStore.IsConfigured();

        var token = FirebaseAuthTokenStore.TryLoad();
        return token != null && token.IsUsable();
    }

    public static bool EnsureConfigured(Window? owner, out string? errorMessage)
    {
        errorMessage = null;

        if (UseDirectByok)
        {
            if (ReplicateApiTokenStore.IsConfigured())
                return true;

            errorMessage =
                "A Replicate API token is required (BYOK dev mode). Open Help → Developer API Settings to save your token.";
            ReplicateTokenGate.PromptOpenApiSettings(owner);
            return false;
        }

        var token = FirebaseAuthTokenStore.TryLoad();
        if (token != null && token.IsUsable())
            return true;

        errorMessage =
            "Sign in with your CaveAI Pro Google account to use cloud AI rendering. " +
            SubscriptionEntitlementResult.RequiredUserMessage + ".";
        return false;
    }

    public static string StatusText()
    {
        if (UseDirectByok)
        {
            return ReplicateApiTokenStore.IsConfigured()
                ? "Generative AI: BYOK token configured (dev mode)"
                : "Generative AI: BYOK token not configured";
        }

        var token = FirebaseAuthTokenStore.TryLoad();
        return token != null && token.IsUsable()
            ? "Generative AI: signed in — cloud rendering available (subscription required)"
            : "Generative AI: sign in with your CaveAI Pro Google account to use cloud AI rendering";
    }

    public static bool IsAuthError(Exception ex) =>
        ex is FirebaseCallableException fce && (fce.IsUnauthenticated || fce.IsPermissionDenied) ||
        ReplicateTokenGate.IsAuthError(ex);
}
