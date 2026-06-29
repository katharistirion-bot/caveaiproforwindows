using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Legal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class LegalTextsTests
{
    [TestMethod]
    public void PrivacySummary_matches_distribution_channel()
    {
        var expected = DistributionChannel.UpdatesHandledByStore
            ? LegalTexts.PrivacySummaryStore
            : LegalTexts.PrivacySummarySideload;

        Assert.AreEqual(expected, LegalTexts.PrivacySummary);
    }

    [TestMethod]
    public void PrivacySummarySideload_mentions_github_updates()
    {
        StringAssert.Contains(LegalTexts.PrivacySummarySideload, "GitHub Releases");
        StringAssert.Contains(LegalTexts.PrivacySummarySideload, "Velopack");
    }

    [TestMethod]
    public void PrivacySummaryStore_mentions_microsoft_store_updates()
    {
        StringAssert.Contains(LegalTexts.PrivacySummaryStore, "Microsoft Store");
        Assert.IsFalse(LegalTexts.PrivacySummaryStore.Contains("GitHub", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(LegalTexts.PrivacySummaryStore.Contains("Velopack", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FullDisclaimer_contains_FCRPA_protected_cave_data_clause()
    {
        const string clause =
            "By using this application, you agree that you will not upload, share, or distribute any private, restricted, or federally protected cave data (including but not limited to data protected under the U.S. Federal Cave Resources Protection Act - FCRPA, or local archaeological and environmental laws worldwide). You acknowledge that you retain full and exclusive legal responsibility for any data you import or share through this platform.";

        StringAssert.Contains(LegalTexts.FullDisclaimerAndEula, clause);
        StringAssert.Contains(LegalTexts.FullDisclaimerAndEula, "13C. PROTECTED CAVE DATA (FCRPA & LOCAL LAWS):");
    }

    [TestMethod]
    public void DocumentVersion_is_1_4()
    {
        Assert.AreEqual("1.4", LegalTexts.DocumentVersion);
    }

    [TestMethod]
    public void PrivacySummary_mentions_on_device_ai_not_cloud_render()
    {
        StringAssert.Contains(LegalTexts.PrivacySummarySideload, "On-device Cave AI");
        Assert.IsFalse(LegalTexts.PrivacySummarySideload.Contains("AI Render", StringComparison.OrdinalIgnoreCase));
    }
}
