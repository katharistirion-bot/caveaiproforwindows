using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class MapExportQualityTests
{
    [TestMethod]
    public void Standard_quality_uses_300_dpi_scale()
    {
        Assert.AreEqual(300.0 / 96.0, MapExportQuality.Standard.DpiScale(), 1e-9);
        Assert.AreEqual(PlanMapRasterExporter.ExportDpiScale, MapExportQuality.Standard.DpiScale(), 1e-9);
    }

    [TestMethod]
    public void Print_quality_uses_600_dpi_scale()
    {
        Assert.AreEqual(600.0 / 96.0, MapExportQuality.Print.DpiScale(), 1e-9);
    }

    [TestMethod]
    public void Print_quality_doubles_dpi_scale_before_pixel_cap()
    {
        Assert.AreEqual(2.0, MapExportQuality.Print.DpiScale() / MapExportQuality.Standard.DpiScale(), 1e-9);

        var standard = PlanMapRasterExporter.ComputeExportPixelSize(100f, 100f, MapExportQuality.Standard);
        var print = PlanMapRasterExporter.ComputeExportPixelSize(100f, 100f, MapExportQuality.Print);
        Assert.IsTrue(print.pxW >= standard.pxW);
        Assert.IsTrue(print.pxH >= standard.pxH);
    }

    [TestMethod]
    public void Plan_export_is_deterministic_for_minimal_traverse()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var project = new CaveProjectDocument
            {
                Name = "GoldenTraverse",
                Date = "2026-06-30",
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "A", ToStation = "B", Distance = 10, Azimuth = 0, Clino = 0,
                        L = 1, R = 1, U = 1, D = 1,
                    },
                ],
            };
            byte[]? Export() => PlanMapRasterExporter.TryCapturePlanPngHighRes(
                project,
                SurveyVisualizationMode.Standard,
                highContrast: false,
                PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan),
                Array.Empty<PlanRasterUnderlay>(),
                zipPath: null,
                baseLongEdgePixels: 640);
            var a = Export();
            var b = Export();
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            CollectionAssert.AreEqual(a, b);
        });
    }

    [TestMethod]
    public void Compare_overlay_preview_renders_for_two_versions()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var a = new CaveProjectDocument
            {
                Name = "A", Date = "2026-01-01",
                Shots = [new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10, Azimuth = 90, Clino = 0, L = 1, R = 1, U = 1, D = 1 }],
            };
            var b = new CaveProjectDocument
            {
                Name = "A", Date = "2026-01-01",
                Shots = [new ShotRecord { FromStation = "1", ToStation = "2", Distance = 12, Azimuth = 90, Clino = 0, L = 1, R = 1, U = 1, D = 1 }],
            };
            Assert.IsNotNull(CompareBackupPlanPreview.TryRenderOverlayPlan(a, b));
        });
    }
}
