using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Resources["SplashEnter"] is Storyboard enter)
            enter.Begin(this);

        if (Resources["ProgressShimmer"] is Storyboard shimmer)
            shimmer.Begin(this, true);
    }

    private void TryApplyLogo()
    {
        var logo = TryLoadPackImage("pack://application:,,,/Assets/logo.png");
        if (logo == null)
            return;
        LogoImage.Source = logo;
        LogoImage.Visibility = Visibility.Visible;
    }

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

    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(text));
            return;
        }

        StatusLabel.Text = text ?? "";
        StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x95, 0xA8));
    }

    public void ReleaseTopmost()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ReleaseTopmost);
            return;
        }

        Topmost = false;
    }

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
