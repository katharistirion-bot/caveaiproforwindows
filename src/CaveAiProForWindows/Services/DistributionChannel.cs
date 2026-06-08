namespace CaveAiProForWindows.Services;

/// <summary>
/// Compile-time distribution channel. Set <c>STORE_DISTRIBUTION</c> when building for Microsoft Store (MSIX).
/// Sideload / GitHub releases omit the constant and keep Velopack auto-update.
/// </summary>
public static class DistributionChannel
{
#if STORE_DISTRIBUTION
    public const bool IsMicrosoftStoreBuild = true;
#else
    public const bool IsMicrosoftStoreBuild = false;
#endif

    /// <summary>Store delivers updates; in-app Velopack / GitHub checks must not run.</summary>
    public static bool UpdatesHandledByStore => IsMicrosoftStoreBuild;

    /// <summary>Velopack install hooks are for sideload Setup.exe only.</summary>
    public static bool UseVelopackBootstrap => !IsMicrosoftStoreBuild;
}
