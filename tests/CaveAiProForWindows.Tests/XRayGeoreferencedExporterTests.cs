using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class XRayGeoreferencedExporterTests
{
    [TestMethod]
    public void ExportPngWithWorldFile_writes_pgw_and_prj()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai_geo_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var basePath = Path.Combine(dir, "xray_map");
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
            var bbox = new XRayBackdropMetadata(
                MinLat: 39.99,
                MaxLat: 40.01,
                MinLon: 21.99,
                MaxLon: 22.01,
                SourceLabel: "test");

            XRayGeoreferencedExporter.ExportPngWithWorldFile(basePath, png, bbox, 1024, 512);

            Assert.IsTrue(File.Exists(basePath + ".png"));
            Assert.IsTrue(File.Exists(basePath + ".pgw"));
            Assert.IsTrue(File.Exists(basePath + ".prj"));
            var pgw = File.ReadAllLines(basePath + ".pgw");
            Assert.AreEqual(6, pgw.Length);
            Assert.IsTrue(double.Parse(pgw[0], System.Globalization.CultureInfo.InvariantCulture) > 0);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }
}
