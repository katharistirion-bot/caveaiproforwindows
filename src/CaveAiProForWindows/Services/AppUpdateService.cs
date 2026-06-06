using System.Reflection;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace CaveAiProForWindows.Services;

/// <summary>Checks GitHub Releases for Velopack updates (Setup.exe channel from release.yml).</summary>
public static class AppUpdateService
{
    /// <summary>Override with env <c>CAVEAIPRO_GITHUB_REPO</c> (owner/repo).</summary>
    public const string DefaultGitHubRepo = "katharistirion-bot/caveaiproforwindows";

    public static string GitHubRepo
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CAVEAIPRO_GITHUB_REPO");
            return string.IsNullOrWhiteSpace(env) ? DefaultGitHubRepo : env.Trim();
        }
    }

    public static async Task CheckForUpdatesOnStartupAsync(Window? owner)
    {
#if DEBUG
        return;
#else
        await CheckForUpdatesAsync(owner, silent: true).ConfigureAwait(false);
#endif
    }

    /// <summary>Help → Check for updates (manual, shows feedback when up to date).</summary>
    public static async Task CheckForUpdatesAsync(Window? owner, bool silent = false)
    {
        try
        {
            var repoUrl = $"https://github.com/{GitHubRepo}";
            var source = new GithubSource(repoUrl, string.Empty, false);
            var mgr = new UpdateManager(source);

            if (!mgr.IsInstalled)
            {
                if (!silent)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            owner,
                            "Auto-update is available after installing with the Velopack Setup.exe or MSI from GitHub Releases.",
                            "CAVE AI PRO — Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information));
                }

                return;
            }

            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update == null)
            {
                if (!silent)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            owner,
                            $"You are on the latest version ({CurrentVersion}).",
                            "CAVE AI PRO — Updates",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information));
                }

                return;
            }

            await mgr.DownloadUpdatesAsync(update).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(
                    owner,
                    $"Version {update.TargetFullRelease.Version} is available. Restart now to install?",
                    "CAVE AI PRO — Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    mgr.ApplyUpdatesAndRestart(update);
            });
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Update check failed: " + ex.Message);
            if (!silent)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        owner,
                        "Could not check for updates.\n\n" + ex.Message,
                        "CAVE AI PRO — Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning));
            }
        }
    }

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
