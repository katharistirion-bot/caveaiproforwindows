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
    /// <param name="includeSectionSheetInk">
    /// False for procedural assist, which must not copy the sheet's own stamps back onto the design layer.
    /// The read-only section and the long profile still draw that ink.
    /// </param>
    public static PlanScene? TryBuild(
        CaveProjectDocument project,
        SurveyVisualizationMode visualization,
        bool includeSectionSheetInk = true) =>
        ExtendedElevationSceneBuilder.TryBuild(project, visualization, includeSectionSheetInk);
}
