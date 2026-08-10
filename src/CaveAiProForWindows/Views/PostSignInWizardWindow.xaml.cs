using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Localization;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class PostSignInWizardWindow : Window
{
    private int _step;
    private const int LastStep = 3;

    public PostSignInWizardWindow()
    {
        InitializeComponent();
        ApplyStep();
    }

    public static void ShowIfNeeded(Window? owner)
    {
        var settings = AppUiSettingsStore.LoadOrDefault();
        if (settings.HasCompletedPostSignInWizard)
            return;

        var dlg = new PostSignInWizardWindow { Owner = owner };
        dlg.ShowDialog();
        settings.HasCompletedPostSignInWizard = true;
        AppUiSettingsStore.Save(settings);
    }

    private void ApplyStep()
    {
        SyncFolderPanel.Visibility = Visibility.Collapsed;
        SamplePanel.Visibility = Visibility.Collapsed;
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step >= LastStep ? AppStrings.OnboardingFinish : AppStrings.OnboardingNext;

        switch (_step)
        {
            case 0:
                StepTitle.Text = AppStrings.PostSignInStep0Title;
                StepBody.Text = AppStrings.PostSignInStep0Body;
                SyncFolderPanel.Visibility = Visibility.Visible;
                SyncFolderBox.Text = AppUiSettingsStore.LoadOrDefault().AndroidSync.SyncFolderPath ?? "";
                break;
            case 1:
                StepTitle.Text = AppStrings.PostSignInStep1Title;
                StepBody.Text = AppStrings.PostSignInStep1Body;
                SamplePanel.Visibility = Visibility.Visible;
                break;
            case 2:
                StepTitle.Text = AppStrings.PostSignInStep2Title;
                StepBody.Text = AppStrings.PostSignInStep2Body;
                break;
            default:
                StepTitle.Text = AppStrings.PostSignInStep3Title;
                StepBody.Text = AppStrings.PostSignInStep3Body;
                break;
        }
    }

    private void BrowseSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Android sync folder" };
        if (dlg.ShowDialog(this) == true)
            SyncFolderBox.Text = dlg.FolderName;
    }

    private void OpenSample_Click(object sender, RoutedEventArgs e)
    {
        var sample = AppContentPaths.Assets("Certification", "certification-demo-survey.json");
        if (!File.Exists(sample))
        {
            MessageBox.Show(this, "Demo survey not found in app folder.", "Sample", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Owner?.DataContext is ViewModels.MainViewModel vm)
            vm.LoadFromPaths(new[] { sample });
        DialogResult = true;
    }

    private void SaveSyncFolder()
    {
        var path = SyncFolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return;
        var settings = AppUiSettingsStore.LoadOrDefault();
        settings.AndroidSync.SyncFolderPath = path;
        AppUiSettingsStore.Save(settings);
        AndroidDesktopSyncHub.NotifySyncSettingsChanged();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
            SaveSyncFolder();

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
