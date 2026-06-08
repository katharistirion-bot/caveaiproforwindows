using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows;

/// <summary>
/// Branded startup splash. Shown by <see cref="App"/> while background services and sign-in run,
/// then closed when the main window is ready or an error is shown.
/// </summary>
public partial class SplashScreen : Window
{
    public SplashScreen()
    {
        InitializeComponent();
        VersionLabel.Text = "Version " + ResolveDisplayVersion();
        TryApplyLogo();
    }

    private void TryApplyLogo()
    {
        var logo = TryLoadPackImage("pack://application:,,,/Assets/logo.png");
        if (logo == null)
            return;
        LogoImage.Source = logo;
        LogoImage.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Loads embedded PNG without pack-URI MIME sniffing (avoids FindMimeFromData TypeLoadException on some builds).
    /// </summary>
    internal static ImageSource? TryLoadPackImage(string packUri)
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute))?.Stream;
            if (stream == null)
                return null;
            using (stream)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Updates the small status line under the progress bar from background work.</summary>
    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text));
            return;
        }

        StatusLabel.Text = text ?? "";
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x89));
    }

    /// <summary>Allow the sign-in window to appear above the splash.</summary>
    public void ReleaseTopmost()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ReleaseTopmost);
            return;
        }

        Topmost = false;
    }

    /// <summary>Stops the indeterminate progress animation.</summary>
    public void StopProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(StopProgress);
            return;
        }

        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 0;
    }

    /// <summary>Stops the spinner and shows a user-facing error on the splash.</summary>
    public void ShowError(string title, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowError(title, message));
            return;
        }

        StopProgress();
        Topmost = false;
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));
        StatusLabel.Text = string.IsNullOrWhiteSpace(title)
            ? message
            : title + ": " + message;
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
