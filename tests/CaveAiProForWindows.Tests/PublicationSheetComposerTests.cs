using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.PublicationSheet;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class PublicationSheetComposerTests
{
    private static CaveProjectDocument SampleTraverseProject() =>
        new()
        {
            Name = "SheetTestCave",
            Date = "2026-01-01",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 12,
                    Azimuth = 0,
                    Clino = -30,
                    L = 2f,
                    R = 1.5f,
                    U = 1f,
                    D = 0.8f,
                },
                new ShotRecord
                {
                    FromStation = "B",
                    ToStation = "C",
                    Distance = 9,
                    Azimuth = 90,
                    Clino = -10,
                    L = 1.2f,
                    R = 1.8f,
                    U = 1.5f,
                    D = 1f,
                },
            ],
        };

    [TestMethod]
    public void Stats_from_traverse_project_has_length_and_stations()
    {
        var stats = PublicationSheetStats.TryFromProject(SampleTraverseProject());
        Assert.IsNotNull(stats);
        Assert.AreEqual(3, stats!.StationCount);
        Assert.AreEqual(2, stats.TraverseLegCount);
        Assert.IsTrue(stats.SurveyLengthMetres > 20);
    }

    [TestMethod]
    public void Compose_minimal_traverse_project_returns_png()
    {
        var result = RunOnStaThread(() => PublicationSheetComposer.TryCompose(
            SampleTraverseProject(),
            new PublicationSheetOptions
            {
                Include3DOverview = true,
                Quality = MapExportQuality.Standard,
            }));

        Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
        Assert.IsNotNull(result.PngBytes);
        Assert.IsTrue(result.PngBytes!.Length > 5000);
        Assert.AreEqual(0x89, result.PngBytes[0]);
    }

    [TestMethod]
    public void Compose_empty_project_fails_without_geometry()
    {
        var result = RunOnStaThread(() => PublicationSheetComposer.TryCompose(
            new CaveProjectDocument { Name = "Empty" },
            new PublicationSheetOptions { Include3DOverview = false }));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    private static T RunOnStaThread<T>(Func<T> work)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        if (error != null)
            throw error;
        return result!;
    }
}
