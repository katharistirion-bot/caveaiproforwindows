using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Exports a Therion project folder: centerline <c>.th</c>, plan/section <c>.th2</c> maps with walls + symbols, and <c>thconfig</c>.
/// </summary>
public static class TherionProjectExporter
{
    public sealed class ExportResult
    {
        public required string FolderPath { get; init; }
        public required IReadOnlyList<string> WrittenFiles { get; init; }
    }

    public static ExportResult ExportProjectFolder(CaveProjectDocument project, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        var surveyId = TherionExporterSanitize.SurveyId(project.Name);
        var written = new List<string>();

        var surveyPath = Path.Combine(folderPath, $"{surveyId}.th");
        File.WriteAllBytes(surveyPath, TherionExporter.BuildCenterlineThUtf8Bom(project));
        written.Add(surveyPath);

        var planTh2 = Path.Combine(folderPath, $"{surveyId}-plan.th2");
        File.WriteAllText(planTh2, BuildMapTh2(project, surveyId, "plan", SurveyStationGeometry.AndroidViewModePlan), Encoding.UTF8);
        written.Add(planTh2);

        var sectionPolys = SurveyStationGeometry.ParseVectorLinesForViewMode(
            project.VectorLines, SurveyStationGeometry.AndroidViewModeSection);
        var includeSection = sectionPolys.Count > 0 ||
                             SurveyStationGeometry.ParseSectionMapSymbols(project).Count > 0;
        if (includeSection)
        {
            var sectionTh2 = Path.Combine(folderPath, $"{surveyId}-section.th2");
            File.WriteAllText(
                sectionTh2,
                BuildMapTh2(project, surveyId, "extended", SurveyStationGeometry.AndroidViewModeSection),
                Encoding.UTF8);
            written.Add(sectionTh2);
        }

        var configPath = Path.Combine(folderPath, "thconfig");
        File.WriteAllText(configPath, BuildThConfig(surveyId, includeSection), Encoding.UTF8);
        written.Add(configPath);

        var readmePath = Path.Combine(folderPath, "README-Therion.txt");
        File.WriteAllText(readmePath, BuildReadme(project, surveyId), Encoding.UTF8);
        written.Add(readmePath);

        return new ExportResult { FolderPath = folderPath, WrittenFiles = written };
    }

    private static string BuildThConfig(string surveyId, bool includeSection)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Cave AI Pro — minimal Therion compile config");
        sb.AppendLine($"source {surveyId}-plan.th2");
        if (includeSection)
            sb.AppendLine($"source {surveyId}-section.th2");
        sb.AppendLine($"export map-plan-{surveyId}.pdf -layout plan -projection plan -output output");
        return sb.ToString();
    }

    private static string BuildReadme(CaveProjectDocument project, string surveyId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Cave AI Pro → Therion export");
        sb.AppendLine($"Project: {project.Name}");
        sb.AppendLine();
        sb.AppendLine("Files:");
        sb.AppendLine($"  {surveyId}.th          — survey centerline + splays");
        sb.AppendLine($"  {surveyId}-plan.th2    — plan map (walls + symbols)");
        sb.AppendLine($"  {surveyId}-section.th2 — section map (if section vectors exist)");
        sb.AppendLine("  thconfig               — example compile sources");
        sb.AppendLine();
        sb.AppendLine("Compile (XTherion): open thconfig or run therion on the .th2 files.");
        sb.AppendLine("Verify station names, fix/CS, and symbol types before publishing.");
        return sb.ToString();
    }

    private static string BuildMapTh2(
        CaveProjectDocument project,
        string surveyId,
        string projection,
        int viewMode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("encoding utf-8");
        sb.AppendLine($"# Cave AI Pro map — {projection} (viewMode {viewMode})");
        sb.AppendLine();

        var mapId = viewMode == SurveyStationGeometry.AndroidViewModePlan
            ? $"{surveyId}-plan"
            : $"{surveyId}-section";

        sb.AppendLine($"map {mapId} -projection {projection}");
        sb.AppendLine($"  survey {surveyId}");
        sb.AppendLine("    m0");
        sb.AppendLine("  endsurvey");
        sb.AppendLine("  scrap m0 -scale 100");

        AppendWalls(sb, project, viewMode);
        AppendSymbols(sb, project, viewMode);

        sb.AppendLine("  endscrap");
        sb.AppendLine("endmap");
        return sb.ToString();
    }

    private static void AppendWalls(StringBuilder sb, CaveProjectDocument project, int viewMode)
    {
        var polys = new List<SurveyStationGeometry.PlanVectorPolyline>();
        polys.AddRange(SurveyStationGeometry.ParseVectorLinesForViewMode(project.VectorLines, viewMode));
        if (viewMode == SurveyStationGeometry.AndroidViewModePlan)
        {
            polys.AddRange(SurveyStationGeometry.ParsePlanSketches(project));
            polys.AddRange(SurveyStationGeometry.ParsePlanSectionSketchesInPlan(project));
        }

        if (polys.Count == 0)
            return;

        sb.AppendLine("    wall");
        foreach (var poly in polys)
        {
            if (poly.Points.Count < 2)
                continue;
            foreach (var (x, y) in poly.Points)
                sb.AppendLine($"      {Fmt(x)} {Fmt(y)}");
            if (poly.Closed && poly.Points.Count > 0)
            {
                var (fx, fy) = poly.Points[0];
                sb.AppendLine($"      {Fmt(fx)} {Fmt(fy)}");
            }
        }

        sb.AppendLine("    endwall");
    }

    private static void AppendSymbols(StringBuilder sb, CaveProjectDocument project, int viewMode)
    {
        var symbols = viewMode == SurveyStationGeometry.AndroidViewModePlan
            ? SurveyStationGeometry.ParsePlanMapSymbols(project)
            : SurveyStationGeometry.ParseSectionMapSymbols(project);

        foreach (var sym in symbols)
        {
            var therionType = TherionSymbolMapper.ToTherionPointType(sym.IconKey, sym.SymbolId, sym.Label);
            sb.AppendLine($"    point {Fmt(sym.X)} {Fmt(sym.Y)} 0");
            if (!string.IsNullOrWhiteSpace(sym.Label))
                sb.AppendLine($"      label \"{EscapeLabel(sym.Label)}\"");
            sb.AppendLine($"      {therionType}");
            sb.AppendLine("    endpoint");
        }

        if (viewMode == SurveyStationGeometry.AndroidViewModePlan)
        {
            var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
            foreach (var auto in CaveMappingSymbolPlacer.BuildAutoPlanSymbols(project, coords, project.Shots))
            {
                sb.AppendLine($"    point {Fmt(auto.X)} {Fmt(auto.Y)} 0");
                if (!string.IsNullOrWhiteSpace(auto.Label))
                    sb.AppendLine($"      label \"{EscapeLabel(auto.Label)}\"");
                sb.AppendLine($"      {TherionSymbolMapper.ToTherionPointType(auto.IconKey, auto.SymbolId, auto.Label)}");
                sb.AppendLine("    endpoint");
            }
        }
    }

    private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string EscapeLabel(string label) =>
        label.Replace("\"", "'", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

/// <summary>Shared Therion id sanitizers (also used by <see cref="TherionExporter"/>).</summary>
internal static class TherionExporterSanitize
{
    internal static string SurveyId(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name.Trim(), @"[^a-zA-Z0-9_]+", "_");
        if (string.IsNullOrEmpty(s)) s = "cave";
        if (char.IsDigit(s[0])) s = "s_" + s;
        return s.ToLowerInvariant();
    }
}
