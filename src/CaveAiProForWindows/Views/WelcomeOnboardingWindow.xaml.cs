using System.Windows;

using CaveAiProForWindows.Services;

using CaveAiProForWindows.Services.Localization;



namespace CaveAiProForWindows.Views;



public partial class WelcomeOnboardingWindow : Window

{

    private int _step;

    private const int LastStep = 4;



    public WelcomeOnboardingWindow()

    {

        InitializeComponent();

        ApplyStep();

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



    private void ApplyStep()

    {

        TitleBlock.Text = AppStrings.OnboardingTitle;

        SkipButton.Content = AppStrings.OnboardingSkip;

        BackButton.Content = AppStrings.OnboardingBack;

        NextButton.Content = _step >= LastStep ? AppStrings.OnboardingFinish : AppStrings.OnboardingNext;

        BackButton.IsEnabled = _step > 0;



        switch (_step)
        {
            case 0:
                StepTitleBlock.Text = AppStrings.OnboardingStep0Title;
                StepBodyBlock.Text = AppStrings.OnboardingStep0Body;
                break;
            case 1:
                StepTitleBlock.Text = AppStrings.OnboardingStep1Title;
                StepBodyBlock.Text = AppStrings.OnboardingStep1Body;
                break;
            case 2:
                StepTitleBlock.Text = AppStrings.OnboardingStep2Title;
                StepBodyBlock.Text = AppStrings.OnboardingStep2Body;
                break;
            case 3:
                StepTitleBlock.Text = AppStrings.OnboardingStep3Title;
                StepBodyBlock.Text = AppStrings.OnboardingStep3Body;
                break;
            default:
                StepTitleBlock.Text = AppStrings.OnboardingStep4Title;
                StepBodyBlock.Text = AppStrings.OnboardingStep4Body;
                break;
        }

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

