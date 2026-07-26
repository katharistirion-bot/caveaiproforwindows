using System.Globalization;
using System.Text;
using CaveAiProForWindows.Services.SurveyLoopQc;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.PublishReadiness;

/// <summary>Rules-only publish readiness — contract: docs/publish-readiness-contract.json</summary>
public static class PublishReadinessEvaluator
{
    public static PublishReadinessResult Evaluate(PublishReadinessContext ctx)
    {
        var items = new List<PublishReadinessItem>();
        foreach (var id in RuleOrder)
        {
            var item = EvaluateRule(id, ctx);
            if (item != null)
                items.Add(item);
        }

        var scored = items.Where(i => i.Tier != PublishReadinessTier.Info).ToList();
        var ready = scored.Count(i => i.Done);
        var total = scored.Count;
        var required = items.Where(i => i.Required).ToList();
        var recommended = items.Where(i => i.Tier == PublishReadinessTier.Recommended).ToList();
        var percent = total > 0 ? (int)Math.Round(ready * 100.0 / total, MidpointRounding.AwayFromZero) : 0;

        return new PublishReadinessResult
        {
            Items = items,
            ReadyCount = ready,
            TotalCount = total,
            ScorePercent = percent,
            RequiredDone = required.All(i => i.Done),
            RecommendedDone = recommended.All(i => i.Done),
        };
    }

    public static int CountCriticalTopologyIssues(CaveProjectDocument? project)
    {
        if (project == null)
            return 0;
        return TraverseQcStats.BuildSurveyQcIssueRows(project).Count(r =>
            r.Category.Contains("Topology", StringComparison.OrdinalIgnoreCase) &&
            !r.Message.Contains("OK", StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatSummaryLine(PublishReadinessResult result) =>
        $"Publish readiness {result.ScorePercent}% ({result.ReadyCount}/{result.TotalCount})";

    public static string FormatConfirmationMessage(PublishReadinessContext ctx)
    {
        var result = Evaluate(ctx);
        var sb = new StringBuilder();
        sb.AppendLine("Before publishing to the Public Cave Library:");
        sb.AppendLine();
        sb.AppendLine(FormatSummaryLine(result));
        sb.AppendLine();
        foreach (var item in result.Items.Where(i => i.Tier != PublishReadinessTier.Info))
        {
            var mark = item.Done ? "OK" : item.Required ? "!" : "o";
            var detail = string.IsNullOrWhiteSpace(item.Detail) ? "" : $" - {item.Detail}";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{mark} {item.Label}{detail}");
        }
        sb.AppendLine();
        sb.AppendLine("Rules-based checklist only - verify on web before submitting.");
        sb.AppendLine();
        sb.Append("Continue with publish?");
        return sb.ToString().TrimEnd();
    }

    private static readonly string[] RuleOrder =
    [
        "legal_terms",
        "library_link",
        "publisher_profile",
        "cave_name",
        "entrance_gps",
        "survey_json",
        "survey_structure",
        "traverse_qc_topology",
        "loop_misclosure",
        "description",
        "photos_in_survey",
    ];

    private static PublishReadinessItem? EvaluateRule(string ruleId, PublishReadinessContext ctx)
    {
        var project = ctx.Project;
        var shots = project?.Shots ?? [];
        var topologyCritical = ctx.TopologyQcCriticalCount ?? CountCriticalTopologyIssues(project);
        var photoCount = shots.Sum(s => s.Photos?.Count ?? 0);
        var linkedId = (ctx.LinkedLibraryCaveId ?? (project != null ? LinkedLibraryCaveIdResolver.TryGet(project) : null) ?? "").Trim();

        return ruleId switch
        {
            "legal_terms" when ctx.Mode == PublishReadinessMode.WindowsCloud => Item(
                ruleId, PublishReadinessTier.Required, "Legal terms accepted",
                "Accept disclaimer in LEGAL & SETTINGS before cloud publish.",
                ctx.LegalTermsAccepted, ctx.LegalTermsAccepted ? null : "Open LEGAL & SETTINGS"),
            "library_link" when ctx.Mode == PublishReadinessMode.WindowsCloud => Item(
                ruleId, PublishReadinessTier.Required, "Public Library link",
                "Firestore published_caves document id (?cave= on the web map).",
                !string.IsNullOrWhiteSpace(linkedId), linkedId),
            "publisher_profile" when ctx.Mode == PublishReadinessMode.WindowsCloud && ctx.ProfileComplete.HasValue => Item(
                ruleId, PublishReadinessTier.Required, "Publisher profile",
                "First name, last name, and country — complete at caveaipro.com/account/profile.",
                ctx.ProfileComplete.Value,
                ctx.ProfileComplete.Value ? null : "Open account profile in browser"),
            "cave_name" => Item(
                ruleId, PublishReadinessTier.Required, "Cave name",
                "Shown in browse, map popups, and library search.",
                !string.IsNullOrWhiteSpace(project?.Name?.Trim())),
            "entrance_gps" when ctx.Mode != PublishReadinessMode.WebSync => Item(
                ruleId, PublishReadinessTier.Required, "Entrance GPS",
                "Latitude and longitude place the cave on the global map.",
                HasEntranceGps(project)),
            "survey_json" => Item(
                ruleId, PublishReadinessTier.Required, "Survey JSON",
                "survey_project.json with at least one shot.",
                shots.Count > 0, shots.Count > 0 ? $"{shots.Count} shot(s)" : null),
            "survey_structure" => Item(
                ruleId, PublishReadinessTier.Required, "Survey structure valid",
                "Project loads with survey geometry.",
                project != null && shots.Count > 0 && !string.IsNullOrWhiteSpace(project.Name),
                project == null ? "No project" : null),
            "traverse_qc_topology" when ctx.Mode is PublishReadinessMode.WindowsCloud or PublishReadinessMode.WebWorkspace => Item(
                ruleId, PublishReadinessTier.Recommended, "Traverse topology QC",
                "Resolve critical topology issues in SURVEY QC before publishing.",
                topologyCritical == 0,
                topologyCritical == 0 ? "No critical issues" : $"{topologyCritical} critical issue(s) - review SURVEY QC tab",
                required: false),
            "loop_misclosure" when ctx.Mode is PublishReadinessMode.WindowsCloud or PublishReadinessMode.WebWorkspace or PublishReadinessMode.WebPublish => EvaluateLoopMisclosure(project),
            "description" => Item(
                ruleId, PublishReadinessTier.Recommended, "Description",
                "Short summary for Public Library search and share cards.",
                !string.IsNullOrWhiteSpace(ctx.Description),
                required: false),
            "photos_in_survey" => Item(
                ruleId, PublishReadinessTier.Recommended, "Photos in survey",
                "Gallery photos improve dossier and publish quality.",
                photoCount > 0,
                photoCount > 0 ? $"{photoCount} photo(s)" : "None in survey",
                required: false),
            _ => null,
        };
    }

    private static PublishReadinessItem EvaluateLoopMisclosure(CaveProjectDocument? project)
    {
        if (project == null)
        {
            return Item(
                "loop_misclosure", PublishReadinessTier.Recommended, "Loop misclosure",
                "Close large traverse loops (worst |Δ| ≥ 1 m) in Survey QC before publishing.",
                true, "No survey loaded", required: false);
        }

        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        if (loops.Count == 0)
        {
            return Item(
                "loop_misclosure", PublishReadinessTier.Recommended, "Loop misclosure",
                "Close large traverse loops (worst |Δ| ≥ 1 m) in Survey QC before publishing.",
                true, "No loops detected", required: false);
        }

        var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
        var label = SurveyLoopQcSummary.SeverityLabel(worst.MisclosureMeters, worst.PathLengthMeters);
        var done = !string.Equals(label, "large", StringComparison.Ordinal);
        var detail = done
            ? $"Worst |Δ|={worst.MisclosureMeters.ToString("0.##", CultureInfo.InvariantCulture)} m ({label})"
            : $"Worst |Δ|={worst.MisclosureMeters.ToString("0.##", CultureInfo.InvariantCulture)} m (large) — review closing shots";
        return Item(
            "loop_misclosure", PublishReadinessTier.Recommended, "Loop misclosure",
            "Close large traverse loops (worst |Δ| ≥ 1 m) in Survey QC before publishing.",
            done, detail, required: false);
    }

    private static bool HasEntranceGps(CaveProjectDocument? project) =>
        project is { Lat: not null, Lon: not null } &&
        Math.Abs(project.Lat.Value) <= 90 &&
        Math.Abs(project.Lon.Value) <= 180 &&
        !(Math.Abs(project.Lat.Value) < 1e-9 && Math.Abs(project.Lon.Value) < 1e-9);

    private static PublishReadinessItem Item(
        string id,
        PublishReadinessTier tier,
        string label,
        string hint,
        bool done,
        string? detail = null,
        bool? required = null) =>
        new()
        {
            Id = id,
            Tier = tier,
            Label = label,
            Hint = hint,
            Done = done,
            Required = required ?? tier == PublishReadinessTier.Required,
            Detail = detail,
        };
}
