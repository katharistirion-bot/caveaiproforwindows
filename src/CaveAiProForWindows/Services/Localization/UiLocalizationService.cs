using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.ViewModels;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.Localization;

/// <summary>Applies localized UI strings to main window chrome.</summary>
public static class UiLocalizationService
{
    public static string CurrentLanguage { get; private set; } = "en";

    public static bool IsGreek =>
        string.Equals(CurrentLanguage, "el", StringComparison.OrdinalIgnoreCase);

    public static void LoadLanguageFromSettings()
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        CurrentLanguage = string.Equals(all.UiLanguage, "el", StringComparison.OrdinalIgnoreCase) ? "el" : "en";
    }

    private static string L(string en, string el) => IsGreek ? el : en;

    public static void ApplyToMainWindow(MainWindow window, MainViewModel vm)
    {
        if (window.FindName("MenuFile") is MenuItem file)
            file.Header = L("File", "\u0391\u03c1\u03c7\u03b5\u03af\u03bf");
        if (window.FindName("MenuHelp") is MenuItem help)
            help.Header = L("Help", "\u0392\u03bf\u03ae\u03b8\u03b5\u03b9\u03b1");
        if (window.FindName("MenuOpen") is MenuItem open)
            open.Header = L("Open\u2026", "\u0386\u03bd\u03bf\u03b9\u03b3\u03bc\u03b1\u2026");
        if (window.FindName("MenuSave") is MenuItem save)
            save.Header = L("Save project", "\u0391\u03c0\u03bf\u03b8\u03ae\u03ba\u03b5\u03c5\u03c3\u03b7 \u03ad\u03c1\u03b3\u03bf\u03c5");
        if (window.FindName("MenuExit") is MenuItem exit)
            exit.Header = L("Exit", "\u0388\u03be\u03bf\u03b4\u03bf\u03c2");
        if (window.FindName("MenuAbout") is MenuItem about)
            about.Header = L("About", "\u03a3\u03c7\u03b5\u03c4\u03b9\u03ba\u03ac");
        if (window.FindName("MenuCheckUpdates") is MenuItem updates)
            updates.Header = AppStrings.MenuCheckUpdates;
        if (window.FindName("MenuTools") is MenuItem tools)
            tools.Header = AppStrings.MenuTools;
        if (window.FindName("ToolbarOpenButton") is System.Windows.Controls.Button tbOpen)
            tbOpen.Content = L("Open", "\u0386\u03bd\u03bf\u03b9\u03b3\u03bc\u03b1");
        if (window.FindName("AndroidSyncReloadButton") is System.Windows.Controls.Button reloadBtn)
            reloadBtn.Content = L("Reload now", "\u0395\u03c0\u03b1\u03bd\u03b1\u03c6\u03cc\u03c1\u03c4\u03c9\u03c3\u03b7");
        if (window.FindName("AndroidSyncDismissButton") is System.Windows.Controls.Button dismissBtn)
            dismissBtn.Content = L("Dismiss", "\u0391\u03c0\u03cc\u03c1\u03c1\u03b9\u03c8\u03b7");

        vm.WindowTitle = L("CAVE AI PRO \u2014 Survey workstation", "CAVE AI PRO \u2014 \u03a3\u03c4\u03b1\u03b8\u03bc\u03cc\u03c2 survey");
        vm.StatusMessage = L(
            AppStrings.StatusReady,
            "\u0388\u03c4\u03bf\u03b9\u03bc\u03bf \u2014 \u03b1\u03bd\u03bf\u03af\u03be\u03c4\u03b5 backup (.json/.zip) \u03b3\u03b9\u03b1 QC \u03ba\u03b1\u03b9 \u03b5\u03be\u03b1\u03b3\u03c9\u03b3\u03ad\u03c2.");
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
        if (planView.FindName("Viewport3DCompetitivePresetButton") is System.Windows.Controls.Button competitiveBtn)
            competitiveBtn.Content = AppStrings.Viewport3DCompetitivePreset;
    }
}
