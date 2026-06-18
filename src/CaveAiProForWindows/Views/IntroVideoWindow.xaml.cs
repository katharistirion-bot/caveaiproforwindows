using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Cinematic first-run intro: bundled MP4 when present, animated fallback otherwise.
/// Ends on the CAVE AI PRO logo; Skip and Esc always available.
/// </summary>
public partial class IntroVideoWindow : Window
{
    private static readonly TimeSpan LogoHoldDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FallbackAutoAdvance = TimeSpan.FromSeconds(5);

    private bool _closing;
    private bool _logoEndShown;
    private bool _usingVideo;
    private DispatcherTimer? _fallbackTimer;

    public IntroVideoWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _fallbackTimer?.Stop();
    }

    public static void ShowIfFirstRun(Window? owner)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (!IntroVideoGate.ShouldShowFirstRun(settings))
            return;

        var dlg = new IntroVideoWindow { Owner = owner };
        dlg.ShowDialog();

        settings.HasSeenIntroVideo = true;
        AppUiSettingsStore.Save(settings);
    }

    public static void ShowReplay(Window? owner)
    {
        var dlg = new IntroVideoWindow { Owner = owner };
        dlg.ShowDialog();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var videoPath = IntroVideoAssetPaths.TryResolveExistingPath();
        if (!string.IsNullOrEmpty(videoPath))
        {
            _usingVideo = true;
            VideoPlayer.Visibility = Visibility.Visible;
            LetterboxTop.Visibility = Visibility.Visible;
            LetterboxBottom.Visibility = Visibility.Visible;
            VideoPlayer.Source = new Uri(videoPath, UriKind.Absolute);
            VideoPlayer.Play();
            return;
        }

        StartFallbackSequence();
    }

    private void StartFallbackSequence()
    {
        _usingVideo = false;
        MuteButton.Visibility = Visibility.Collapsed;
        FallbackPanel.Visibility = Visibility.Visible;
        ((Storyboard)Resources["FallbackPulse"]).Begin(this, true);
        ((Storyboard)Resources["FallbackTitleEnter"]).Begin(this, true);

        _fallbackTimer = new DispatcherTimer { Interval = FallbackAutoAdvance };
        _fallbackTimer.Tick += (_, _) =>
        {
            _fallbackTimer.Stop();
            ShowLogoEndFrame();
        };
        _fallbackTimer.Start();
    }

    private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Volume = 1;
        VideoPlayer.IsMuted = false;
        UpdateMuteLabel();
    }

    private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e) => ShowLogoEndFrame();

    private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (!_usingVideo || _logoEndShown)
            return;

        _usingVideo = false;
        VideoPlayer.Visibility = Visibility.Collapsed;
        VideoPlayer.Stop();
        VideoPlayer.Source = null;
        MuteButton.Visibility = Visibility.Collapsed;
        StartFallbackSequence();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => ShowLogoEndFrame();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ShowLogoEndFrame();
            e.Handled = true;
        }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (!_usingVideo)
            return;

        VideoPlayer.IsMuted = !VideoPlayer.IsMuted;
        UpdateMuteLabel();
    }

    private void UpdateMuteLabel() =>
        MuteButton.Content = VideoPlayer.IsMuted ? "Unmute" : "Mute";

    private void ShowLogoEndFrame()
    {
        if (_logoEndShown)
            return;

        _logoEndShown = true;
        _fallbackTimer?.Stop();

        SkipButton.IsEnabled = false;
        MuteButton.IsEnabled = false;

        if (_usingVideo)
        {
            VideoPlayer.Stop();
            VideoPlayer.Visibility = Visibility.Collapsed;
        }

        FallbackPanel.Visibility = Visibility.Collapsed;
        LogoEndFrame.Visibility = Visibility.Visible;

        var fade = (Storyboard)Resources["LogoFadeIn"];
        fade.Completed += (_, _) => HoldLogoThenClose();
        fade.Begin(this);
    }

    private void HoldLogoThenClose()
    {
        var hold = new DispatcherTimer { Interval = LogoHoldDuration };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            CloseIntro();
        };
        hold.Start();
    }

    private void CloseIntro()
    {
        if (_closing)
            return;

        _closing = true;
        DialogResult = true;
    }
}
