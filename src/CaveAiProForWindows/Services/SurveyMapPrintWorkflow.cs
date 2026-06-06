using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Services;

/// <summary>Captures a map visual and opens <see cref="Views.PrintPreviewWindow"/>.</summary>
public static class SurveyMapPrintWorkflow
{
    public static void ShowPreview(
        Window? owner,
        Models.CaveProjectDocument project,
        SurveyMapPrintKind kind,
        FrameworkElement captureRoot,
        bool highContrast = false,
        string? subtitleSuffix = null,
        SurveyMapPrintContext? cartography = null,
        Action? afterPreview = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(captureRoot);

        var mapImage = SurveyMapPrintCapture.CaptureElement(captureRoot);
        var request = new SurveyMapPrintRequest
        {
            Project = project,
            Kind = kind,
            MapImage = mapImage,
            HighContrast = highContrast,
            SubtitleSuffix = subtitleSuffix,
            Cartography = cartography,
        };

        SurveyMapPrintPreviewService.ShowPreview(owner, request);
        afterPreview?.Invoke();
    }
}
