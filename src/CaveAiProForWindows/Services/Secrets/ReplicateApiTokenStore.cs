namespace CaveAiProForWindows.Services.Secrets;

/// <summary>
/// Replicate API token: Windows Credential Manager first, then <c>CAVEAIPRO_REPLICATE_API_TOKEN</c> env var.
/// </summary>
public static class ReplicateApiTokenStore
{
    public const string CredentialTargetName = "CaveAiProForWindows:ReplicateApiToken";

    public const string EnvironmentVariableName = "CAVEAIPRO_REPLICATE_API_TOKEN";

    public static bool IsConfigured() => !string.IsNullOrWhiteSpace(TryRead());

    public static string? TryRead()
    {
        if (WindowsCredentialSecretStore.TryRead(CredentialTargetName, out var stored) &&
            !string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        var env = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    public static bool TrySave(string token) =>
        WindowsCredentialSecretStore.TryWrite(
            CredentialTargetName,
            token.Trim(),
            "Replicate API token for Cave AI Pro generative map rendering");

    public static bool TryClear() => WindowsCredentialSecretStore.TryDelete(CredentialTargetName);
}
