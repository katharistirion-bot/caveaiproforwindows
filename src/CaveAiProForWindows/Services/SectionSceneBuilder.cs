using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Section (viewMode 1) scene graph — same <see cref="PlanScene"/> container as plan; vectors/sketches filtered inside <see cref="PlanSceneBuilder"/>.
/// LRUD passage ribbon and X-ray splays are built in <see cref="PlanSceneBuilder"/> using the same plan-frame reduction as the traverse centerline.
/// </summary>
public static class SectionSceneBuilder
{
    public static PlanScene? TryBuild(CaveProjectDocument project) =>
        PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModeSection);

    /// <summary>Section tab with visualization (Standard / SectionNight / SectionXRay).</summary>
    public static PlanScene? TryBuild(CaveProjectDocument project, SurveyVisualizationMode visualization) =>
        PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModeSection, visualization);
}
