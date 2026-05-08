using System.Reflection;
using System.Windows;

namespace CaveAiProForWindows;

/// <summary>
/// Branded startup splash. Shown by <see cref="App"/> for ~2.5s while background services warm up,
/// then closed when the main window is ready.
/// </summary>
public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        VersionLabel.Text = "Version " + ResolveDisplayVersion();
    }

    /// <summary>Updates the small status line under the indeterminate progress bar from background work.</summary>
    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text));
            return;
        }

        StatusLabel.Text = text ?? "";
    }

    private static string ResolveDisplayVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v == null)
                return "1.0";
            return $"{v.Major}.{v.Minor}";
        }
        catch
        {
            return "1.0";
        }
    }
}
