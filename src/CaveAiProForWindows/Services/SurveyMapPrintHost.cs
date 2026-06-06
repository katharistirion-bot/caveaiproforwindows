using System.Windows;
using System.Windows.Controls;

namespace CaveAiProForWindows.Services;

/// <summary>Routes print preview from the main shell to the active Plan / Section / X-Ray tab.</summary>
public static class SurveyMapPrintHost
{
    public static bool CanPrint(TabControl? surveyTabs) =>
        surveyTabs?.SelectedContent is ISurveyMapPrintSurface;

    public static void TryShowPreview(TabControl? surveyTabs, Window owner)
    {
        if (surveyTabs?.SelectedContent is ISurveyMapPrintSurface surface)
        {
            surface.ShowPrintPreview();
            return;
        }

        MessageBox.Show(
            owner,
            "Open the PLAN, SECTION, or X-RAY survey tab to print the active map.",
            "Print preview",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
