using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Section (viewMode 1) scene graph — same <see cref="PlanScene"/> container as plan, but coordinates are
/// <b>extended elevation</b> (developed distance × Z) from <see cref="ExtendedElevationSceneBuilder"/>, not plan X/Y.
/// </summary>
public static class SectionSceneBuilder
{
    public static PlanScene? TryBuild(CaveProjectDocument project) =>
        ExtendedElevationSceneBuilder.TryBuild(project, SurveyVisualizationMode.Standard);

    /// <summary>Section tab with visualization (Standard / SectionNight / SectionXRay).</summary>
    public static PlanScene? TryBuild(CaveProjectDocument project, SurveyVisualizationMode visualization) =>
        ExtendedElevationSceneBuilder.TryBuild(project, visualization);
}
