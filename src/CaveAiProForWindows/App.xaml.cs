using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CaveAiProForWindows.Services;
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

        try
        {
            var splash = ShowSplash();
            WaitForSplashWarmup(splash, MinimumSplashDuration);

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            WriteStartupLog("MainWindow shown");
            if (startupSurveyPaths.Count > 0 && main.DataContext is MainViewModel vm)
                vm.LoadFromPaths(startupSurveyPaths);

            CloseSplash(splash);

            _ = AppUpdateService.CheckForUpdatesOnStartupAsync(main);
        }
        catch (Exception ex)
        {
            WriteFatalLog("MainWindow startup failed", ex);
            try
            {
                System.Windows.MessageBox.Show(
                    FormatUserFacingError(ex),
                    "CAVE AI PRO — startup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                /* ignore */
            }

            Shutdown(1);
            return;
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
            // Pump the dispatcher once so the splash actually paints before MainWindow construction blocks the UI thread.
            splash.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            return splash;
        }
        catch (Exception ex)
        {
            WriteStartupLog("Splash creation failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Blocks until the splash has been on screen at least <paramref name="minimum"/>, while keeping the
    /// dispatcher alive so the indeterminate progress bar continues animating.
    /// </summary>
    private static void WaitForSplashWarmup(SplashScreen? splash, TimeSpan minimum)
    {
        if (splash == null)
            return;
        var sw = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, splash.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40),
        };
        timer.Tick += (_, _) =>
        {
            if (sw.Elapsed >= minimum)
            {
                timer.Stop();
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void CloseSplash(SplashScreen? splash)
    {
        if (splash == null)
            return;
        try
        {
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
            var sb = new StringBuilder();
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
