using System.Windows;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.Legal;

/// <summary>
/// Blocks application startup until the user accepts the current legal document version.
/// Aligns with Android DisclaimerScreen and web DisclaimerLaunchModal.
/// </summary>
public static class LegalTermsLaunchGate
{
    /// <summary>True when stored acceptance is missing or not for LegalTexts.DocumentVersion.</summary>
    public static bool NeedsAcceptance => !LegalTermsAcceptanceStore.Load();

    /// <summary>
    /// Shows LegalDisclaimerLaunchWindow when needed. Must run on the WPF UI thread.
    /// </summary>
    public static bool TryEnsureAccepted(Window? owner = null)
    {
        if (!NeedsAcceptance)
        {
            App.WriteStartupLog(
                $"Legal gate: already accepted document v{LegalTexts.DocumentVersion}");
            return true;
        }

        App.WriteStartupLog(
            $"Legal gate: showing disclaimer (document v{LegalTexts.DocumentVersion})");

        var dlg = new LegalDisclaimerLaunchWindow { Owner = owner };
        if (dlg.ShowDialog() != true)
        {
            App.WriteStartupLog("Legal gate: user declined or closed");
            return false;
        }

        LegalTermsAcceptanceStore.Save(true);
        App.WriteStartupLog(
            $"Legal gate: accepted document v{LegalTexts.DocumentVersion}, UTC {DateTime.UtcNow:O}");
        return true;
    }
}