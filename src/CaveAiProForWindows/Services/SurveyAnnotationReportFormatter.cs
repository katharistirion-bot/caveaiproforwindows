using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Text summary of survey map overlays for office reports and cloud publish metadata.</summary>
public static class SurveyAnnotationReportFormatter
{
    public static string BuildOverlaySummary(CaveProjectDocument? project)
    {
        if (project == null)
            return "No project.";

        var sb = new StringBuilder();
        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan);
        if (scene == null)
        {
            sb.AppendLine("No plan geometry for overlay summary.");
            return sb.ToString().TrimEnd();
        }

        var ann = SurveyMapAnnotationsBuilder.Build(project, scene, 0, legDetails: true, stationEnvironment: true);
        var pins = FieldCatalogMapPinCollector.Collect(project);
        var loops = SurveyLoopClosureHighlighter.Detect(project);

        sb.AppendLine($"Cave name: {CaveProjectDisplayNames.GetDisplayName(project)}");
        sb.AppendLine($"Stations: {scene.Stations.Count}  ·  Traverse legs: {scene.TraverseSegments.Count}");
        sb.AppendLine($"Leg labels: {ann.LegLabels.Count}  ·  Station environment: {ann.StationEnvironment.Count}");
        sb.AppendLine($"Depth spans: {ann.DepthSpans.Count}  ·  Brackets: {ann.Brackets.Count}");
        sb.AppendLine($"Field catalog map pins: {pins.Count}  ·  Map symbols: {scene.Symbols.Count}");
        sb.AppendLine($"Loop-closing legs (topology): {loops.Count}");
        if (loops.Count > 0)
        {
            foreach (var loop in loops.Take(8))
                sb.AppendLine($"  · {loop.FromStation} → {loop.ToStation}  misclosure ≈ {loop.MisclosureMeters:0.##} m");
            if (loops.Count > 8)
                sb.AppendLine($"  … +{loops.Count - 8} more");
        }

        return sb.ToString().TrimEnd();
    }
}
