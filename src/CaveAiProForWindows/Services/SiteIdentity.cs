using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Unified site identity for active survey projects — mirrors Android <c>SiteIdentity.kt</c> and web <c>siteIdentity.js</c>.
/// English labels only.
/// </summary>
public sealed record ResolvedSiteIdentity(
    string Layer,
    string SiteTypeToken,
    string SiteTypeLabel,
    string PrimaryMapLabel,
    string SummaryLine);

public static class SiteIdentity
{
    public const string LayerActiveSurvey = "active_survey";

    public static ResolvedSiteIdentity ResolveActiveProject(
        CaveProjectDocument? project,
        IEnumerable<KnownCaveRecord>? library = null)
    {
        if (project == null)
        {
            return new ResolvedSiteIdentity(
                LayerActiveSurvey,
                "",
                "",
                "Cave",
                "Site identity: no active project loaded.");
        }

        var kind = SurveySiteTypeResolver.Resolve(project, library);
        if (kind == SurveySiteTypeKind.Unknown)
            kind = SurveySiteTypeKind.Cave;

        var token = SurveySiteType.CanonicalToken(kind);
        if (string.IsNullOrEmpty(token))
        {
            token = "CAVE";
            kind = SurveySiteTypeKind.Cave;
        }

        var label = SurveySiteType.GetMapLabel(kind);
        if (string.IsNullOrEmpty(label))
            label = "Cave";

        var summary = $"Site identity: active CaveAI Pro field survey ({label}).";
        if (ReferenceSurveyLinkService.TryGetLink(project, out var link) && link != null)
        {
            var refName = string.IsNullOrWhiteSpace(link.Name) ? link.Id : link.Name.Trim();
            summary += $" Linked to reference catalog pin \"{refName}\" (id {link.Id.Trim()}).";
        }

        return new ResolvedSiteIdentity(
            LayerActiveSurvey,
            token,
            label,
            label,
            summary);
    }

    public static string SummarizeActiveProject(
        CaveProjectDocument? project,
        IEnumerable<KnownCaveRecord>? library = null) =>
        ResolveActiveProject(project, library).SummaryLine;
}
