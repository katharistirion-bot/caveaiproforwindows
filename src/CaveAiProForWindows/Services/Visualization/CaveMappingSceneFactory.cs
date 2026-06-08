using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>
/// Single entry point that builds 2D/3D scene descriptors from the local <see cref="CaveProjectDocument"/> database.
/// Wraps existing clean-room geometry (<see cref="PlanSceneBuilder"/>, <see cref="SurveyLrudWallGeometry"/>).
/// </summary>
public static class CaveMappingSceneFactory
{
    public static CaveMappingSceneDescriptor Build(CaveProjectDocument project, CaveMappingViewKind kind)
    {
        ArgumentNullException.ThrowIfNull(project);

        return kind switch
        {
            CaveMappingViewKind.PlanDrafting2D => Build2D(
                project,
                kind,
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.Standard),

            CaveMappingViewKind.ExtendedProfile2D => Build2D(
                project,
                kind,
                SurveyStationGeometry.AndroidViewModeSection,
                SurveyVisualizationMode.Standard),

            CaveMappingViewKind.LongProfile2D => Build2D(
                project,
                kind,
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.LongProfile),

            CaveMappingViewKind.Topography3D => new CaveMappingSceneDescriptor
            {
                ViewKind = kind,
                Project = project,
                Scene2D = null,
                VisualizationMode = SurveyVisualizationMode.Pseudo3D,
                AndroidVectorViewMode = SurveyStationGeometry.AndroidViewModePlan,
            },

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static CaveMappingSceneDescriptor Build2D(
        CaveProjectDocument project,
        CaveMappingViewKind kind,
        int vectorViewMode,
        SurveyVisualizationMode visualization)
    {
        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode, visualization);
        return new CaveMappingSceneDescriptor
        {
            ViewKind = kind,
            Project = project,
            Scene2D = scene,
            VisualizationMode = visualization,
            AndroidVectorViewMode = vectorViewMode,
        };
    }
}
