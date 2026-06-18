using System.Text;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Before Cloud Publish / Republish — suggests index-only reference catalog matches.</summary>
public static class ReferencePublishMatchDialog
{
    public static bool ConfirmProceed(Window? owner, CaveProjectDocument project, IReadOnlyList<ReferenceCaveIndexEntry> index)
    {
        if (ReferenceSurveyLinkService.TryGetLink(project, out _))
            return true;

        var matches = ReferenceCatalogMatchService.FindMatches(project, index, maxResults: 5);
        if (matches.Count == 0)
            return true;

        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.PublishReferenceMatchShown);

        var sb = new StringBuilder();
        sb.AppendLine($"Before publishing \"{project.Name}\", these reference catalog caves may match (index only):");
        sb.AppendLine();
        foreach (var m in matches)
            sb.AppendLine("• " + ReferenceCatalogMatchService.FormatMatchSummary(m.Entry, m.DistanceKm));
        sb.AppendLine();
        sb.Append("Continue publishing without linking? Choose No to cancel and link from Help → Public Cave Library.");

        var result = MessageBox.Show(
            owner,
            sb.ToString(),
            "Reference catalog suggestions",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return result == MessageBoxResult.Yes;
    }
}
