using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class DistributionChannelTests
{
    [TestMethod]
    public void UseVelopackBootstrap_is_inverse_of_store_build()
    {
        Assert.AreEqual(!DistributionChannel.IsMicrosoftStoreBuild, DistributionChannel.UseVelopackBootstrap);
    }

    [TestMethod]
    public void UpdatesHandledByStore_matches_store_build_flag()
    {
        Assert.AreEqual(DistributionChannel.IsMicrosoftStoreBuild, DistributionChannel.UpdatesHandledByStore);
    }
}
