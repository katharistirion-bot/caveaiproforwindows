using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Immutable snapshot of one sketch-assist capture (survey scene + user ink + screen layout).</summary>
public sealed class SketchAssistSession
{
    public required CaveProjectDocument Project { get; init; }

    public required PlanScene Scene { get; init; }

    public required SketchAssistDocument Document { get; init; }

    /// <summary>
    /// Survey ↔ canvas mapping for the on-screen map size (<see cref="SourceCanvasWidth"/> ×
    /// <see cref="SourceCanvasHeight"/>).
    /// </summary>
    public required PlanCanvasSurveyLayout ScreenLayout { get; init; }

    public double SourceCanvasWidth { get; init; }

    public double SourceCanvasHeight { get; init; }
}
