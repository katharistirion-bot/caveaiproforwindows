using CaveAiProForWindows.Services.Collaboration;

namespace CaveAiProForWindows.Services;

/// <summary>Registers a stable desktop token after Firebase sign-in (collaboration push fan-out).</summary>
public static class CollaborationDeviceTokenRegistrar
{
    private static readonly object Gate = new();
    private static bool _registeredThisSession;

    public static void TryRegisterAfterSignIn()
    {
        lock (Gate)
        {
            if (_registeredThisSession)
                return;
            _registeredThisSession = true;
        }

        _ = RegisterAsync();
    }

    private static async Task RegisterAsync()
    {
        try
        {
            var token = $"windows-{Environment.MachineName}-{Environment.UserName}".ToLowerInvariant();
            token = new string(token.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
            if (token.Length > 120)
                token = token[..120];

            var client = new ProjectCollaborationClient();
            await client.RegisterDeviceTokenAsync(token, "windows").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Collaboration] RegisterDeviceToken skipped: {ex.Message}");
        }
    }
}