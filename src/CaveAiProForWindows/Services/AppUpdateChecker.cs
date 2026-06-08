using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services;

/// <summary>Startup and manual update checks (delegates to <see cref="AppUpdateService"/>).</summary>
public static class AppUpdateChecker
{
    public static Task CheckOnStartupAsync(System.Windows.Window? owner) =>
        AppUpdateService.CheckForUpdatesOnStartupAsync(owner);

    public static Task CheckAsync(System.Windows.Window? owner, bool silent = false) =>
        AppUpdateService.CheckForUpdatesAsync(owner, silent);
}
