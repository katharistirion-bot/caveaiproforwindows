using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Localization;

namespace CaveAiProForWindows.Views;

public partial class WelcomeOnboardingWindow : Window
{
    private int _step;
    private const int LastStep = 4;

    private static readonly string[] StepIcons = ["📱", "📂", "📐", "✨", "🏷️"];

    public WelcomeOnboardingWindow()
    {
        InitializeComponent();
        ApplyStep(animate: false);
    }

    public static void ShowIfFirstRun(Window? owner)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (settings.HasCompletedOnboarding)
            return;

        var dlg = new WelcomeOnboardingWindow { Owner = owner };
        dlg.ShowDialog();

        settings.HasCompletedOnboarding = true;
        AppUiSettingsStore.Save(settings);
    }

    private void ApplyStep(bool animate = true)
    {
        TitleBlock.Text = AppStrings.OnboardingTitle;
        SkipButton.Content = AppStrings.OnboardingSkip;
        BackButton.Content = AppStrings.OnboardingBack;
        NextButton.Content = _step >= LastStep ? AppStrings.OnboardingFinish : AppStrings.OnboardingNext;
        BackButton.IsEnabled = _step > 0;

        StepIconBlock.Text = StepIcons[Math.Clamp(_step, 0, StepIcons.Length - 1)];
        UpdateStepDots();

        string title;
        string body;
        switch (_step)
        {
            case 0:
                title = AppStrings.OnboardingStep0Title;
                body = AppStrings.OnboardingStep0Body;
                break;
            case 1:
                title = AppStrings.OnboardingStep1Title;
                body = AppStrings.OnboardingStep1Body;
                break;
            case 2:
                title = AppStrings.OnboardingStep2Title;
                body = AppStrings.OnboardingStep2Body;
                break;
            case 3:
                title = AppStrings.OnboardingStep3Title;
                body = AppStrings.OnboardingStep3Body;
                break;
            default:
                title = AppStrings.OnboardingStep4Title;
                body = AppStrings.OnboardingStep4Body;
                break;
        }

        if (animate)
            FadeStepContent(() =>
            {
                StepTitleBlock.Text = title;
                StepBodyBlock.Text = body;
            });
        else
        {
            StepTitleBlock.Text = title;
            StepBodyBlock.Text = body;
        }
    }

    private void UpdateStepDots()
    {
        var dots = new[] { Dot0, Dot1, Dot2, Dot3, Dot4 };
        for (var i = 0; i < dots.Length; i++)
        {
            var dot = dots[i];
            dot.Width = i == _step ? 22 : 8;
            dot.Fill = new SolidColorBrush(i == _step
                ? Color.FromRgb(0x5B, 0x9D, 0xFF)
                : Color.FromRgb(0x2A, 0x33, 0x44));
        }
    }

    private void FadeStepContent(Action apply)
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
        fadeOut.Completed += (_, _) =>
        {
            apply();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180));
            StepBodyBlock.BeginAnimation(OpacityProperty, fadeIn);
            StepTitleBlock.BeginAnimation(OpacityProperty, fadeIn);
        };
        StepBodyBlock.BeginAnimation(OpacityProperty, fadeOut);
        StepTitleBlock.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step >= LastStep)
        {
            DialogResult = true;
            return;
        }

        _step++;
        ApplyStep();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
        {
            _step--;
            ApplyStep();
        }
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
