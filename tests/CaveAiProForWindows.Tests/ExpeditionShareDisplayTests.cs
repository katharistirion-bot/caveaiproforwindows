using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ExpeditionShare;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ExpeditionShareDisplayTests
{
    [TestMethod]
    public void ComputeDisplayStatus_overdue_then_hidden()
    {
        var share = new ExpeditionShareDisplay.ExpeditionShareRow
        {
            LeaderUid = "u1",
            Status = "active",
            ExpectedExitAtMs = 1_000L,
        };
        Assert.AreEqual(ExpeditionShareDisplay.DisplayStatus.Active, ExpeditionShareDisplay.ComputeDisplayStatus(share, 500L));
        Assert.AreEqual(ExpeditionShareDisplay.DisplayStatus.Overdue, ExpeditionShareDisplay.ComputeDisplayStatus(share, 2_000L));
        Assert.AreEqual(
            ExpeditionShareDisplay.DisplayStatus.Hidden,
            ExpeditionShareDisplay.ComputeDisplayStatus(share, 2_000L + ExpeditionShareDisplay.StaleBufferMs));
    }

    [TestMethod]
    public void CaveKeyForProject_prefers_published_doc()
    {
        var project = new CaveProjectDocument { Name = "Demo" };
        Assert.AreEqual("pub:doc-1", ExpeditionShareDisplay.CaveKeyForProject(project, "doc-1"));
        project.LinkedLibraryCaveId = "card-1";
        Assert.AreEqual("ref:card-1", ExpeditionShareDisplay.CaveKeyForProject(project));
    }
}