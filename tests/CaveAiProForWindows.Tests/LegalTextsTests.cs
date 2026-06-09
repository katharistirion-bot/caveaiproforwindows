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
}
