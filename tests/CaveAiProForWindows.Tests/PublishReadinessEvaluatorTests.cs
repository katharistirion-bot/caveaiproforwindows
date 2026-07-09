using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.PublishReadiness;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class PublishReadinessEvaluatorTests
{
    [TestMethod]
    public void Windows_cloud_readiness_requires_legal_link_gps_and_shots()
    {
        var project = SampleProject();
        var result = PublishReadinessEvaluator.Evaluate(new PublishReadinessContext
        {
            Mode = PublishReadinessMode.WindowsCloud,
            Project = project,
            LegalTermsAccepted = true,
            LinkedLibraryCaveId = "uid_local1",
        });

        Assert.IsTrue(result.RequiredDone);
        Assert.IsTrue(result.ReadyCount >= 5);
        Assert.IsTrue(result.ScorePercent >= 50);
    }

    [TestMethod]
    public void FormatConfirmationMessage_includes_score_line()
    {
        var message = PublishReadinessEvaluator.FormatConfirmationMessage(new PublishReadinessContext
        {
            Mode = PublishReadinessMode.WindowsCloud,
            Project = SampleProject(),
            LegalTermsAccepted = true,
            LinkedLibraryCaveId = "uid_local1",
        });

        StringAssert.Contains(message, "Publish readiness");
        StringAssert.Contains(message, "Rules-based checklist only");
    }

    private static CaveProjectDocument SampleProject() => new()
    {
        Name = "Demo Cave",
        Lat = 38.2,
        Lon = 20.6,
        Shots =
        [
            new ShotRecord
            {
                FromStation = "1",
                ToStation = "2",
                Photos = ["photo.jpg"],
            },
        ],
    };
}
