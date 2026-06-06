using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>Host callbacks from <see cref="Views.SketchEditorView"/> for persistence.</summary>
public sealed class SketchEditorPersistenceHost
{
    public required Func<CaveProjectDocument?> GetProject { get; init; }

    public required Func<Canvas?> GetDesignLayer { get; init; }

    public required Func<PlanCanvasSurveyLayout> GetSurveyLayout { get; init; }

    public required Func<bool> IsSurveyLayoutReady { get; init; }

    public required Func<byte[]?> CaptureStructureMask { get; init; }

    public required Func<string?> GetPrimarySourcePath { get; init; }
}
