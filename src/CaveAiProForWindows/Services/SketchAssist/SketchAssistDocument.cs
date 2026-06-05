namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>
/// Session-local sketch assist state — all geometry in survey metres (same frame as
/// <see cref="PlanScene"/> / Android <c>data.json</c> plan coordinates).
/// </summary>
public sealed class SketchAssistDocument
{
    public string ProjectName { get; init; } = "";

    public string? ProjectDate { get; init; }

    /// <summary>Axis-aligned survey bounds copied from the source <see cref="PlanScene"/>.</summary>
    public float SurveyMinX { get; init; }

    public float SurveyMaxX { get; init; }

    public float SurveyMinY { get; init; }

    public float SurveyMaxY { get; init; }

    /// <summary>Freehand ink converted from the WPF <c>DesignLayer</c>.</summary>
    public List<SketchStrokeModel> UserStrokes { get; init; } = new();

    /// <summary>Symbol-tool stamps (optional mask input).</summary>
    public List<SketchSymbolStampModel> UserSymbolStamps { get; init; } = new();

    public float SurveySpanX => Math.Max(1e-6f, SurveyMaxX - SurveyMinX);

    public float SurveySpanY => Math.Max(1e-6f, SurveyMaxY - SurveyMinY);
}
