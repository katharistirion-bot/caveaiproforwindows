namespace CaveAiProForWindows.Services;

/// <summary>Survey layout metadata used to draw print-quality scale bar and compass overlays.</summary>
public sealed class SurveyMapPrintContext
{
    public double? SourcePxPerMetre { get; init; }

    public SurveyCanvasKind CanvasKind { get; init; } = SurveyCanvasKind.Plan;

    public bool ShowCartographyOverlay { get; init; } = true;
}
