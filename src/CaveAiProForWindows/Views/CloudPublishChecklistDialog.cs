using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.PublishReadiness;

namespace CaveAiProForWindows.Views;

/// <summary>Pre-publish checklist for cloud publish workflow.</summary>
public static class CloudPublishChecklistDialog
{
    public static bool Confirm(Window? owner, CaveProjectDocument project, bool legalTermsAccepted)
    {
        var ctx = new PublishReadinessContext
        {
            Mode = PublishReadinessMode.WindowsCloud,
            Project = project,
            LegalTermsAccepted = legalTermsAccepted,
            LinkedLibraryCaveId = LinkedLibraryCaveIdResolver.TryGet(project),
            TopologyQcCriticalCount = PublishReadinessEvaluator.CountCriticalTopologyIssues(project),
        };
        var message = PublishReadinessEvaluator.FormatConfirmationMessage(ctx);

        if (!legalTermsAccepted)
        {
            MessageBox.Show(owner, message, "Publish checklist", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return MessageBox.Show(owner, message, "Publish checklist", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
               MessageBoxResult.Yes;
    }
}
