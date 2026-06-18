using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        WriteStartupLog("OnStartup begin");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);
        ThemePaletteSwitcher.ApplyInitial();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var startupSurveyPaths = CollectStartupSurveyPaths(e.Args);
        if (startupSurveyPaths.Count > 0)
            WriteStartupLog("Startup file args: " + string.Join("; ", startupSurveyPaths));

#if !DEBUG
        if (!InstallationGuard.IsLaunchedFromRegisteredInstall())
        {
            try
            {
                System.Windows.MessageBox.Show(
                    InstallationGuard.BlockedUserMessage,
                    "CAVE AI PRO",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                /* ignore */
            }

            Shutdown(0);
            return;
        }
#endif

        RunGatedStartup(startupSurveyPaths);
    }

    private void RunGatedStartup(List<string> startupSurveyPaths)
    {
        _ = RunGatedStartupAsync(startupSurveyPaths);
    }

    private async Task RunGatedStartupAsync(List<string> startupSurveyPaths)
    {
        SplashScreen? splash = null;
        try
        {
            splash = ShowSplash();
            if (splash != null)
                AppStartupSplashController.Register(splash);

            await WaitForSplashWarmupAsync(splash, MinimumSplashDuration).ConfigureAwait(true);

            splash?.SetStatus("Loading background services…");
            Debug.WriteLine("[Startup] Background warm-up complete — opening app lock.");

            using var gateCts = new CancellationTokenSource(AppStartupGateOptions.UnlockFlowTimeout);
            bool unlocked;
            try
            {
                unlocked = await AppLockBootstrapper.TryEnsureUnlockedAsync(splash, gateCts.Token)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                WriteStartupLog("App lock: timed out waiting for sign-in/subscription");
                splash?.ShowError(
                    "Sign-in timed out",
                    "Could not complete Google sign-in and subscription verification in time. Please try again.");
                await Task.Delay(3500).ConfigureAwait(true);
                Shutdown(0);
                return;
            }

            if (!unlocked)
            {
                WriteStartupLog("App lock: user exited without active subscription");
                Shutdown(0);
                return;
            }

            CloseSplash(splash);
            splash = null;

            var main = new MainWindow();
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            main.Show();
            WriteStartupLog("MainWindow shown");
            Debug.WriteLine("[Startup] MainWindow shown.");

            if (startupSurveyPaths.Count > 0 && main.DataContext is MainViewModel vm)
                vm.LoadFromPaths(startupSurveyPaths);
            else if (MicrosoftTestMode.IsActive && main.DataContext is MainViewModel vmDemo)
            {
                var demoPath = MicrosoftTestMode.ResolveBundledDemoSurveyPath();
                if (demoPath != null)
                {
                    vmDemo.LoadFromPaths(new[] { demoPath });
                    WriteStartupLog("MicrosoftTestMode: loaded bundled demo survey");
                }
                else
                {
                    WriteStartupLog("MicrosoftTestMode: bundled demo survey missing");
                }
            }

            _ = AppUpdateService.CheckForUpdatesOnStartupAsync(main);
        }
        catch (Exception ex)
        {
            WriteFatalLog("MainWindow startup failed", ex);
            Debug.WriteLine("[Startup] FAILED: " + ex);
            splash?.ShowError("Startup failed", ex.Message);
            try
            {
                if (splash == null)
                {
                    System.Windows.MessageBox.Show(
                        FormatUserFacingError(ex),
                        "CAVE AI PRO — startup failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    await Task.Delay(4000).ConfigureAwait(true);
                }
            }
            catch
            {
                /* ignore */
            }

            Shutdown(1);
        }
        finally
        {
            CloseSplash(splash);
            AppStartupSplashController.Clear();
        }

        WriteStartupLog("OnStartup end");
    }

    /// <summary>Minimum on-screen time so the brand splash is actually visible even on fast machines.</summary>
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromMilliseconds(2500);

    private static SplashScreen? ShowSplash()
    {
        try
        {
            var splash = new SplashScreen();
            splash.Show();
            splash.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            return splash;
        }
        catch (Exception ex)
        {
            WriteStartupLog("Splash creation failed: " + ex.Message);
            return null;
        }
    }

    private static async Task WaitForSplashWarmupAsync(SplashScreen? splash, TimeSpan minimum)
    {
        if (splash == null)
            return;

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < minimum)
        {
            await splash.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40).ConfigureAwait(true);
        }
    }

    private static void CloseSplash(SplashScreen? splash)
    {
        if (splash == null)
            return;
        try
        {
            splash.StopProgress();
            splash.Close();
        }
        catch (Exception ex)
        {
            WriteStartupLog("Splash close failed: " + ex.Message);
        }
    }

    /// <summary>Paths from Explorer double-click / &quot;Open with&quot; (shell passes each path as one argument).</summary>
    private static List<string> CollectStartupSurveyPaths(string[] args)
    {
        var list = new List<string>();
        if (args == null || args.Length == 0)
            return list;
        foreach (var raw in args)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (!File.Exists(full))
                continue;
            var ext = Path.GetExtension(full);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!list.Contains(full, StringComparer.OrdinalIgnoreCase))
                    list.Add(full);
            }
        }

        return list;
    }

    internal static void WriteStartupLog(string message)
    {
        try
        {
#if !DEBUG
            message = DiagnosticLogRedactor.RedactLine(message);
#endif
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows");
            Directory.CreateDirectory(dir);
            var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "startup.log"), line);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void WriteFatalLog(string title, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "last-error.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine(new string('-', 60));
            sb.AppendLine(ex.ToString());
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("Inner:");
                sb.AppendLine(ex.InnerException.ToString());
            }

            File.WriteAllText(path, sb.ToString());
        }
        catch
        {
            /* ignore */
        }
    }

    private static string FormatUserFacingError(Exception ex)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "last-error.txt");
        return
            "The application could not start.\r\n\r\n" +
            "Details were saved to:\r\n" + path + "\r\n\r\n" +
            "Summary:\r\n" + ex.Message;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteFatalLog("DispatcherUnhandledException", e.Exception);
        try
        {
            System.Windows.MessageBox.Show(
                FormatUserFacingError(e.Exception),
                "CAVE AI PRO — error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            /* ignore */
        }

        e.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception ex)
            return;

        WriteFatalLog("AppDomain.UnhandledException", ex);
        try
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    FormatUserFacingError(ex),
                    "CAVE AI PRO — error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
        catch
        {
            /* ignore */
        }
    }
}
