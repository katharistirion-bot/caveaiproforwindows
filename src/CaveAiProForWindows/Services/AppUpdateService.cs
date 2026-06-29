using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Checks GitHub Releases for updates (Velopack when installed, else releases JSON + download link).
/// Microsoft Store builds (<see cref="DistributionChannel.UpdatesHandledByStore"/>) skip all update logic —
/// the Store delivers updates automatically.
/// </summary>
public static class AppUpdateService
{
    /// <summary>True when this build delegates updates to Microsoft Store (no Velopack / GitHub checks).</summary>
    public static bool IsUpdateCheckDisabled => DistributionChannel.UpdatesHandledByStore;

    /// <summary>Velopack pack id (<c>vpk pack -u</c>).</summary>
    public const string VelopackAppId = "CaveAiProForWindows";

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

    public static string GitHubRepoUrl => $"https://github.com/{GitHubRepo}";

    public static string CurrentVersion => AppMetadata.InformationalVersion;

    /// <summary>Creates an update manager when Velopack metadata is present; otherwise null.</summary>
    public static UpdateManager? TryCreateUpdateManager()
    {
        try
        {
            return new UpdateManager(CreateGithubSource());
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("UpdateManager unavailable: " + ex.Message);
            return null;
        }
    }

    /// <summary>True when running from a Velopack-managed install (Setup.exe channel).</summary>
    public static bool IsVelopackInstalled()
    {
        try
        {
            var mgr = TryCreateUpdateManager();
            return mgr is { IsInstalled: true };
        }
        catch (NotInstalledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task CheckForUpdatesOnStartupAsync(Window? owner)
    {
#if DEBUG
        return;
#else
        if (IsUpdateCheckDisabled)
            return;

        await CheckPendingRestartAsync(owner).ConfigureAwait(false);
        await CheckForUpdatesAsync(owner, silent: true).ConfigureAwait(false);
#endif
    }

    /// <summary>Latest GitHub release tag when newer than <see cref="CurrentVersion"/>; null when up to date or unreachable.</summary>
    public static async Task<RemoteVersionInfo?> TryFetchRemoteVersionAsync() =>
        await TryFetchLatestReleaseAsync().ConfigureAwait(false) is { } latest &&
        IsRemoteNewer(latest.Version, CurrentVersion)
            ? latest
            : null;

    /// <summary>Latest GitHub release metadata, or null when the API call fails.</summary>
    public static async Task<RemoteVersionInfo?> TryFetchLatestReleaseAsync()
    {
        if (IsUpdateCheckDisabled)
            return null;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var apiUrl = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.ParseAdd("CaveAiProForWindows/" + CurrentVersion);
            using var resp = await http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl)
                ? tagEl.GetString()?.TrimStart('v', 'V') ?? ""
                : "";
            var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var urlEl)
                ? urlEl.GetString() ?? GitHubRepoUrl + "/releases"
                : GitHubRepoUrl + "/releases";
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            return new RemoteVersionInfo(tag, htmlUrl);
        }
        catch
        {
            return null;
        }
    }

    public sealed record RemoteVersionInfo(string Version, string ReleasePageUrl);

    /// <summary>Help → Check for updates (manual, shows feedback when up to date).</summary>
    public static async Task CheckForUpdatesAsync(Window? owner, bool silent = false)
    {
        if (IsUpdateCheckDisabled)
        {
            if (!silent)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        owner,
                        "Updates are delivered automatically through the Microsoft Store.\n\n"
                        + "Open the Microsoft Store app and check Library → Get updates.",
                        "CAVE AI PRO — Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
            }

            return;
        }

        try
        {
            var mgr = TryCreateUpdateManager();
            if (mgr is { IsInstalled: true })
            {
                await CheckPendingRestartAsync(owner).ConfigureAwait(false);
                await CheckVelopackAsync(mgr, owner, silent).ConfigureAwait(false);
                return;
            }

            await CheckGitHubReleasesJsonAsync(owner, silent).ConfigureAwait(false);
        }
        catch (NotInstalledException)
        {
            await CheckGitHubReleasesJsonAsync(owner, silent).ConfigureAwait(false);
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

    private static async Task CheckPendingRestartAsync(Window? owner)
    {
        try
        {
            var mgr = TryCreateUpdateManager();
            if (mgr is not { IsInstalled: true })
                return;

            var pending = mgr.UpdatePendingRestart;
            if (pending == null)
                return;

            App.WriteStartupLog("Update pending restart: " + pending.Version);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var result = MessageBox.Show(
                    owner,
                    $"Version {pending.Version} has been downloaded and is ready to install.\n\nRestart now?",
                    "CAVE AI PRO — Update ready",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                    mgr.ApplyUpdatesAndRestart(pending);
            });
        }
        catch (NotInstalledException)
        {
            /* portable / dev build */
        }
        catch (Exception ex)
        {
            App.WriteStartupLog("Pending update check failed: " + ex.Message);
        }
    }

    private static async Task CheckVelopackAsync(UpdateManager mgr, Window? owner, bool silent)
    {
        UpdateInfo? update;
        try
        {
            update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
        }
        catch (NotInstalledException)
        {
            await CheckGitHubReleasesJsonAsync(owner, silent).ConfigureAwait(false);
            return;
        }

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
            var targetVersion = update.TargetFullRelease.Version;
            var result = MessageBox.Show(
                owner,
                $"Version {targetVersion} is available. Restart now to install?",
                "CAVE AI PRO — Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
                mgr.ApplyUpdatesAndRestart(update);
        });
    }

    private static async Task CheckGitHubReleasesJsonAsync(Window? owner, bool silent)
    {
        var latest = await TryFetchLatestReleaseAsync().ConfigureAwait(false);
        if (latest == null)
        {
            if (!silent)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        owner,
                        "In-app auto-update requires the Velopack Setup.exe install from GitHub Releases.\n\n" +
                        "MSI installs can use Help → Check for updates to open the latest release page.\n\n" +
                        $"Could not read GitHub releases for {GitHubRepo}.",
                        "CAVE AI PRO — Updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information));
            }

            return;
        }

        if (!IsRemoteNewer(latest.Version, CurrentVersion))
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

        if (silent)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = MessageBox.Show(
                owner,
                $"Version {latest.Version} is available on GitHub.\n\n" +
                "Install CaveAiProForWindows-*-Setup.exe for automatic in-app updates, " +
                "or open the release page to download manually?",
                "CAVE AI PRO — Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(latest.ReleasePageUrl)
                {
                    UseShellExecute = true,
                });
            }
        });
    }

    public static bool IsRemoteNewer(string remote, string local)
    {
        if (string.IsNullOrWhiteSpace(remote))
            return false;
        if (!Version.TryParse(NormalizeVersion(remote), out var rv))
            return false;
        if (!Version.TryParse(NormalizeVersion(local), out var lv))
            return true;
        return rv > lv;
    }

    internal static string NormalizeVersion(string v)
    {
        var parts = v.Split('-')[0].Trim().TrimStart('v', 'V').Split('.');
        return parts.Length switch
        {
            1 => parts[0] + ".0.0",
            2 => parts[0] + "." + parts[1] + ".0",
            _ => string.Join('.', parts.Take(3)),
        };
    }

    private static GithubSource CreateGithubSource() =>
        new(GitHubRepoUrl, accessToken: string.Empty, prerelease: false);
}
