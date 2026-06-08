using System;
using System.Linq;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>Survey metadata block for professional map export (title plate, legend footer).</summary>
public sealed record CaveMappingExportMetadata(
    string ProjectName,
    string? SurveyDate,
    string? Surveyor,
    string ExportDate,
    string? SiteTypeMapLabel = null,
    string? ScaleLabel = null,
    string? NorthLabel = null,
    string? SurveyTeam = null)
{
    public static CaveMappingExportMetadata FromProject(
        CaveProjectDocument project,
        IEnumerable<KnownCaveRecord>? library = null,
        double? pxPerMetre = null,
        SurveyCanvasKind canvasKind = SurveyCanvasKind.Plan)
    {
        ArgumentNullException.ThrowIfNull(project);
        var name = CaveProjectDisplayNames.GetDisplayName(project);
        if (string.IsNullOrWhiteSpace(name))
            name = (project.Name ?? "").Trim();

        var surveyDate = string.IsNullOrWhiteSpace(project.Date) ? null : project.Date.Trim();
        var surveyor = TryReadSurveyor(project);
        var surveyTeam = TryReadSurveyTeam(project) ?? surveyor;
        var siteLabel = SurveySiteTypeResolver.GetMapLabel(project, library);
        var scaleLabel = pxPerMetre is > 0
            ? CartographicScaleCalculator.FormatScaleLabel(pxPerMetre.Value)
            : null;
        var northLabel = ResolveNorthLabel(canvasKind);

        return new CaveMappingExportMetadata(
            string.IsNullOrWhiteSpace(name) ? "Cave survey" : name,
            surveyDate,
            surveyor,
            DateTime.Now.ToString("yyyy-MM-dd"),
            string.IsNullOrWhiteSpace(siteLabel) ? null : siteLabel,
            scaleLabel,
            northLabel,
            surveyTeam);
    }

    public string BuildSubtitleLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(SiteTypeMapLabel))
            parts.Add(SiteTypeMapLabel);
        if (!string.IsNullOrWhiteSpace(ScaleLabel))
            parts.Add($"Scale {ScaleLabel}");
        if (!string.IsNullOrWhiteSpace(NorthLabel))
            parts.Add(NorthLabel);
        if (!string.IsNullOrWhiteSpace(SurveyDate))
            parts.Add(SurveyDate);
        if (!string.IsNullOrWhiteSpace(SurveyTeam))
            parts.Add(SurveyTeam);
        else if (!string.IsNullOrWhiteSpace(Surveyor))
            parts.Add(Surveyor);
        parts.Add($"Exported {ExportDate}");
        return string.Join("  ·  ", parts);
    }

    public string BuildTitleBlockLine()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ScaleLabel))
            parts.Add($"Scale {ScaleLabel}");
        if (!string.IsNullOrWhiteSpace(NorthLabel))
            parts.Add(NorthLabel);
        parts.Add($"Printed {ExportDate}");
        if (!string.IsNullOrWhiteSpace(SurveyTeam))
            parts.Add(SurveyTeam);
        return string.Join("  ·  ", parts);
    }

    public static string ResolveNorthLabel(SurveyCanvasKind canvasKind) =>
        canvasKind == SurveyCanvasKind.Section
            ? "North up (+Y)"
            : "North up (+Y = N)";

    private static string? TryReadSurveyTeam(CaveProjectDocument project)
    {
        foreach (var key in new[] { "surveyTeam", "team", "club", "cavers", "surveyors", "party", "surveyor" })
        {
            if (project.ExtensionData != null &&
                project.ExtensionData.TryGetValue(key, out var el))
            {
                var s = ReadJsonString(el)?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }

        return null;
    }

    private static string? TryReadSurveyor(CaveProjectDocument project)
    {
        foreach (var key in new[] { "surveyor", "surveyTeam", "team", "cavers", "surveyors", "party" })
        {
            if (project.ExtensionData != null &&
                project.ExtensionData.TryGetValue(key, out var el))
            {
                var s = ReadJsonString(el)?.Trim();
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }

        var ctx = project.ExportDeviceContext;
        if (ctx != null)
        {
            var model = string.Join(" ", new[] { ctx.Manufacturer, ctx.Model }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (!string.IsNullOrEmpty(model))
                return model;
        }

        return null;
    }

    private static string? ReadJsonString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Array => string.Join(", ",
                el.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            _ => null,
        };
}
