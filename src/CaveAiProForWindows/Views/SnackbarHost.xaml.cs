using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Lightweight non-modal toast / snackbar pinned bottom-right. Used by export / save commands to confirm
/// success without blocking the UI thread (unlike <see cref="MessageBox"/>). Supports an optional action
/// button (e.g. "Reveal") and auto-dismiss.
/// </summary>
public partial class SnackbarHost : UserControl
{
    private readonly DispatcherTimer _autoDismissTimer;
    private Action? _onAction;

    public SnackbarHost()
    {
        InitializeComponent();
        _autoDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _autoDismissTimer.Tick += (_, _) => Dismiss();
    }

    /// <summary>
    /// Pop a message. <paramref name="actionLabel"/> + <paramref name="onAction"/> are optional and render a
    /// secondary button on the right of the message. The snackbar auto-dismisses after
    /// <paramref name="durationMs"/> milliseconds (default 4000).
    /// </summary>
    public void Show(string message, string? actionLabel = null, Action? onAction = null, int durationMs = 4000)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Show(message, actionLabel, onAction, durationMs));
            return;
        }

        MessageText.Text = message ?? "";
        if (!string.IsNullOrWhiteSpace(actionLabel) && onAction != null)
        {
            ActionLabel.Text = actionLabel.ToUpperInvariant();
            ActionButton.Visibility = Visibility.Visible;
            _onAction = onAction;
        }
        else
        {
            ActionButton.Visibility = Visibility.Collapsed;
            _onAction = null;
        }

        Visibility = Visibility.Visible;
        PlayShowAnimation();
        _autoDismissTimer.Stop();
        _autoDismissTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1500, durationMs));
        _autoDismissTimer.Start();
    }

    public void Dismiss()
    {
        _autoDismissTimer.Stop();
        if (Visibility != Visibility.Visible)
            return;
        var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
            Opacity = 1.0;
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void PlayShowAnimation()
    {
        Opacity = 0;
        CardSlide.Y = 20;
        var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220));
        BeginAnimation(OpacityProperty, fade);
        var slide = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        CardSlide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var cb = _onAction;
        Dismiss();
        try
        {
            cb?.Invoke();
        }
        catch
        {
            /* user-supplied action — never crash the UI thread */
        }
    }
}
