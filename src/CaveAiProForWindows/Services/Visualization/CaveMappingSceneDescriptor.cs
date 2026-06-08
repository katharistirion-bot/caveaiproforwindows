using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>Resolved scene payload for a <see cref="CaveMappingViewKind"/> — 2D uses <see cref="PlanScene"/>; 3D is viewport-driven.</summary>
public sealed class CaveMappingSceneDescriptor
{
    public required CaveMappingViewKind ViewKind { get; init; }

    public required CaveProjectDocument Project { get; init; }

    /// <summary>Populated for 2D kinds; null for <see cref="CaveMappingViewKind.Topography3D"/>.</summary>
    public PlanScene? Scene2D { get; init; }

    public SurveyVisualizationMode VisualizationMode { get; init; } = SurveyVisualizationMode.Standard;

    public int AndroidVectorViewMode { get; init; } = SurveyStationGeometry.AndroidViewModePlan;

    public bool Is3D => ViewKind == CaveMappingViewKind.Topography3D;

    public bool HasDrawableGeometry => Is3D || Scene2D != null;
}
