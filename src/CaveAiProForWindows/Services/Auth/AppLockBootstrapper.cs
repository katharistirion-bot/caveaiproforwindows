using System.Diagnostics;
using System.Windows;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.Auth;

/// <summary>
/// Blocks main application UI until Google sign-in succeeds and Firestore confirms paid or trial entitlement.
/// </summary>
public static class AppLockBootstrapper
{
    /// <summary>
    /// Debug builds only: set <c>CAVEAIPRO_SKIP_APP_LOCK=1</c> to bypass the subscription gate locally.
    /// </summary>
    /// <summary>
    /// Debug: <c>CAVEAIPRO_SKIP_APP_LOCK=1</c>. Release Store review MSIX: <see cref="StoreReviewBuild.IsActive"/>.
    /// </summary>
    public static bool IsBypassEnabled
    {
        get
        {
            if (StoreReviewBuild.IsActive)
                return true;

#if DEBUG
            var v = Environment.GetEnvironmentVariable("CAVEAIPRO_SKIP_APP_LOCK");
            return string.Equals(v, "1", StringComparison.Ordinal) ||
                   string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Shows <see cref="LoginWindow"/> until subscription validates or the user exits.
    /// Must run on the WPF UI thread.
    /// </summary>
    public static bool TryEnsureUnlocked(SplashScreen? splash = null) =>
        TryEnsureUnlockedInternal(splash, CancellationToken.None);

    /// <summary>
    /// Async unlock with optional splash status updates and cancellation (startup timeout).
    /// </summary>
    public static Task<bool> TryEnsureUnlockedAsync(
        SplashScreen? splash = null,
        CancellationToken cancellationToken = default)
    {
        if (IsBypassEnabled)
            return Task.FromResult(true);

        return Application.Current.Dispatcher.InvokeAsync(
            () => TryEnsureUnlockedInternal(splash, cancellationToken)).Task;
    }

    private static bool TryEnsureUnlockedInternal(SplashScreen? splash, CancellationToken cancellationToken)
    {
        if (IsBypassEnabled)
        {
            if (StoreReviewBuild.IsActive)
            {
                StoreReviewBuild.ActivateSession();
                Debug.WriteLine("[AppLock] STORE_REVIEW_UNLOCKED — subscription gate bypassed for Store certification.");
                App.WriteStartupLog("AppLock: STORE_REVIEW_UNLOCKED bypass");
            }

            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();

        PrepareSplashForSignIn(splash);
        AppStartupSplashController.DismissForAuthentication();

        Debug.WriteLine("[AppLock] Showing LoginWindow (modal sign-in + subscription gate).");
        App.WriteStartupLog("AppLock: opening LoginWindow");

        LoginWindow? login = null;
        try
        {
            login = new LoginWindow { Owner = null };
            login.StartupCancellationToken = cancellationToken;
            var ok = login.ShowDialog() == true;
            Debug.WriteLine(ok
                ? "[AppLock] LoginWindow closed — subscription verified."
                : "[AppLock] LoginWindow closed — access not granted.");
            App.WriteStartupLog(ok ? "AppLock: unlocked" : "AppLock: denied or cancelled");
            return ok;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[AppLock] Unlock flow cancelled (timeout).");
            App.WriteStartupLog("AppLock: timed out");
            login?.HandleStartupTimeout();
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[AppLock] Unlock flow failed: " + ex.Message);
            App.WriteStartupLog("AppLock: exception — " + ex.Message);
            throw;
        }
    }

    internal static void PrepareSplashForSignIn(SplashScreen? splash)
    {
        if (splash == null)
            return;

        splash.SetStatus(AppStartupGateOptions.SplashSignInStatus);
        splash.ReleaseTopmost();
        splash.Activate();
    }

    /// <inheritdoc cref="TryEnsureUnlocked"/>
    public static Task<bool> EnsureUnlockedAsync(SplashScreen? splash = null) =>
        TryEnsureUnlockedAsync(splash);
}
