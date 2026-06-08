using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
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
}
