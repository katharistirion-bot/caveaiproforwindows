using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.ViewModels;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.Localization;

/// <summary>Applies <see cref="AppStrings"/> to main window chrome.</summary>
public static class UiLocalizationService
{
    public static void LoadLanguageFromSettings()
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        if (!string.Equals(all.UiLanguage, "en", StringComparison.OrdinalIgnoreCase))
        {
            all.UiLanguage = "en";
            AppUiSettingsStore.Save(all);
        }
    }

    public static void ApplyToMainWindow(MainWindow window, MainViewModel vm)
    {
        if (window.FindName("MenuFile") is MenuItem file)
            file.Header = AppStrings.MenuFile;
        if (window.FindName("MenuHelp") is MenuItem help)
            help.Header = AppStrings.MenuHelp;
        if (window.FindName("MenuOpen") is MenuItem open)
            open.Header = AppStrings.MenuOpen;
        if (window.FindName("MenuSave") is MenuItem save)
            save.Header = AppStrings.MenuSave;
        if (window.FindName("MenuExit") is MenuItem exit)
            exit.Header = AppStrings.MenuExit;
        if (window.FindName("MenuAbout") is MenuItem about)
            about.Header = AppStrings.MenuAbout;
        if (window.FindName("MenuCheckUpdates") is MenuItem updates)
            updates.Header = AppStrings.MenuCheckUpdates;
        if (window.FindName("MenuTools") is MenuItem tools)
            tools.Header = AppStrings.MenuTools;
        if (window.FindName("ToolbarOpenButton") is System.Windows.Controls.Button tbOpen)
            tbOpen.Content = AppStrings.ToolbarOpen;
        if (window.FindName("AndroidSyncReloadButton") is System.Windows.Controls.Button reloadBtn)
            reloadBtn.Content = AppStrings.AndroidSyncReloadNow;
        if (window.FindName("AndroidSyncDismissButton") is System.Windows.Controls.Button dismissBtn)
            dismissBtn.Content = AppStrings.AndroidSyncDismiss;

        vm.WindowTitle = "CAVE AI PRO — Survey workstation";
        vm.StatusMessage = AppStrings.StatusReady;
    }

    public static void ApplyToPlanView3DTools(PlanView planView)
    {
        if (planView.FindName("Viewport3DToolsTitle") is TextBlock title)
            title.Text = AppStrings.Viewport3DToolsTitle;
        if (planView.FindName("Viewport3DShowLabelsCheck") is CheckBox showLabels)
            showLabels.Content = AppStrings.Viewport3DShowLabels;
        if (planView.FindName("Viewport3DLabelSizeLabel") is TextBlock sizeLabel)
            sizeLabel.Text = AppStrings.Viewport3DLabelSize;
        if (planView.FindName("Viewport3DResetLabelsButton") is System.Windows.Controls.Button resetBtn)
            resetBtn.Content = AppStrings.Viewport3DResetLabels;
        if (planView.FindName("Viewport3DSurveyLabelsPresetButton") is System.Windows.Controls.Button surveyBtn)
            surveyBtn.Content = AppStrings.Viewport3DSurveyLabels;
        if (planView.FindName("Viewport3DCleanPresetButton") is System.Windows.Controls.Button cleanBtn)
            cleanBtn.Content = AppStrings.Viewport3DCleanPreset;
    }
}
