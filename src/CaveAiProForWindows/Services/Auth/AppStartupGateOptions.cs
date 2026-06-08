namespace CaveAiProForWindows.Services.Auth;

/// <summary>Startup gate timeouts and status messages for splash + login handoff.</summary>
public static class AppStartupGateOptions
{
    /// <summary>Max time to wait for sign-in dialog + subscription validation during startup.</summary>
    public static readonly TimeSpan UnlockFlowTimeout = TimeSpan.FromMinutes(8);

    /// <summary>Per-request Firestore entitlement check (also capped by HttpClient timeout).</summary>
    public static readonly TimeSpan SubscriptionValidationTimeout = TimeSpan.FromSeconds(45);

    public const string SplashSignInStatus = "Opening secure sign-in…";

    public const string SplashValidatingStatus = "Verifying CaveAI Pro subscription…";
}
