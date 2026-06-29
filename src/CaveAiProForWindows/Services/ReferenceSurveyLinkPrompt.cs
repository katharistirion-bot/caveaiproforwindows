using System.Text;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Services;

/// <summary>Suggests linking a loaded survey to a reference catalog cave.</summary>
public static class ReferenceSurveyLinkPrompt
{
    public static void TryPromptForProjects(
        Window? owner,
        IReadOnlyList<CaveProjectDocument> projects,
        IReadOnlyList<ReferenceCaveIndexEntry> index)
    {
        if (projects.Count == 0 || index.Count == 0)
            return;

        foreach (var project in projects)
        {
            if (ReferenceSurveyLinkService.TryGetLink(project, out _))
                continue;

            var matches = ReferenceCatalogMatchService.FindMatches(project, index);
            if (matches.Count == 0)
                continue;

            var best = matches[0];
            if (best.Score >= 0.85 && best.DistanceKm <= 0.5)
            {
                ReferenceSurveyLinkService.SetLink(project, best.Entry);
                continue;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Survey \"{project.Name}\" may match a reference catalog cave:");
            sb.AppendLine();
            sb.AppendLine(ReferenceCatalogMatchService.FormatMatchSummary(best.Entry, best.DistanceKm));
            sb.AppendLine();
            sb.Append("Link this reference id to project metadata?");

            var result = MessageBox.Show(
                owner,
                sb.ToString(),
                "Reference catalog match",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                ReferenceSurveyLinkService.SetLink(project, best.Entry);
        }
    }
}
