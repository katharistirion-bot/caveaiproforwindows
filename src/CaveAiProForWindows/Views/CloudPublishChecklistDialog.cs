using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Pre-publish checklist for cloud publish workflow.</summary>
public static class CloudPublishChecklistDialog
{
    public static bool Confirm(Window? owner, CaveProjectDocument project, bool legalTermsAccepted)
    {
        var qcRows = TraverseQcStats.BuildSurveyQcIssueRows(project);
        var criticalQc = qcRows.Count(r =>
            r.Category.Contains("Topology", StringComparison.OrdinalIgnoreCase) &&
            !r.Message.Contains("OK", StringComparison.OrdinalIgnoreCase));
        var photoCount = project.Shots.Sum(s => s.Photos?.Count ?? 0);

        var message =
            "Before publishing to the Public Cave Library, confirm:\n\n" +
            $"• Traverse QC: {(criticalQc == 0 ? "no critical issues" : $"{criticalQc} critical issue(s) — review SURVEY QC tab")}\n" +
            $"• Legal terms: {(legalTermsAccepted ? "accepted" : "NOT accepted — open LEGAL & SETTINGS")}\n" +
            $"• Photos in survey: {photoCount}\n\n" +
            "Continue with publish?";

        if (!legalTermsAccepted)
        {
            MessageBox.Show(owner, message, "Publish checklist", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return MessageBox.Show(owner, message, "Publish checklist", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
               MessageBoxResult.Yes;
    }
}
