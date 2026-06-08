using System.IO;
using System.IO.Compression;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiRenderSaveServiceTests
{
    [TestMethod]
    public void TryExportAfterRender_writes_zip_to_user_destination_not_INetCache()
    {
        var restrictedDir = Path.Combine(
            Path.GetTempPath(),
            "caveai-export-" + Guid.NewGuid().ToString("N"),
            "INetCache");
        Directory.CreateDirectory(restrictedDir);
        var sourceZip = Path.Combine(restrictedDir, "backup.zip");
        var exportDir = Path.Combine(Path.GetTempPath(), "caveai-export-dest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(exportDir);
        var destZip = Path.Combine(exportDir, "exported.zip");

        var project = new CaveProjectDocument { Name = "ExportCave", Date = "2026-06-07", Shots = [] };
        using (var zip = ZipFile.Open(sourceZip, ZipArchiveMode.Create))
        {
            var jsonEntry = zip.CreateEntry("data.json");
            using (var w = new StreamWriter(jsonEntry.Open()))
                w.Write("""[{"name":"ExportCave","date":"2026-06-07","shots":[]}]""");
        }

        try
        {
            Assert.IsTrue(AiRenderSaveService.TryExportAfterRender(new AiRenderSaveService.ExportAfterRenderRequest
            {
                Project = project,
                PrimarySourcePath = sourceZip,
                AllProjectsInSource = [project],
                AiMapPng = GenerativeAssetPersistenceTests.SamplePngBytes,
                StructureMaskPng = GenerativeAssetPersistenceTests.SamplePngBytes,
                DestinationPath = destZip,
            }));

            Assert.IsTrue(File.Exists(destZip));
            Assert.IsFalse(destZip.Contains("INetCache", StringComparison.OrdinalIgnoreCase));

            using var outZip = ZipFile.OpenRead(destZip);
            Assert.IsNotNull(outZip.GetEntry("export_assets/windows/ExportCave/ai_cartography.png"));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(restrictedDir)!))
                Directory.Delete(Path.GetDirectoryName(restrictedDir)!, recursive: true);
            if (Directory.Exists(exportDir))
                Directory.Delete(exportDir, recursive: true);
        }
    }

    [TestMethod]
    public void SaveZip_uses_staging_under_temp_not_beside_zip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-zipsave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "survey.zip");
        var project = new CaveProjectDocument { Name = "ZipSave", Date = "2026-06-07", Shots = [] };

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var jsonEntry = zip.CreateEntry("data.json");
            using (var w = new StreamWriter(jsonEntry.Open()))
                w.Write("""[{"name":"ZipSave","date":"2026-06-07","shots":[]}]""");
        }

        try
        {
            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = [project],
                PrimarySourcePath = zipPath,
            });

            Assert.IsFalse(File.Exists(zipPath + ".caveai-save.tmp"));
            Assert.IsTrue(File.Exists(zipPath));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
