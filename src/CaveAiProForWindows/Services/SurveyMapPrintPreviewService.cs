using System.Windows;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services;

/// <summary>Opens the print preview window for a captured survey map.</summary>
public static class SurveyMapPrintPreviewService
{
    public static void ShowPreview(Window? owner, SurveyMapPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var window = new PrintPreviewWindow(request)
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        window.ShowDialog();
    }
}
