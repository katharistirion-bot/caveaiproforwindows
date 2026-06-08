using System.IO;
using System.IO.Compression;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ZipAiAssetPersistenceTests
{
    [TestMethod]
    public void SaveZip_embeds_ai_assets_from_extension_paths_when_not_staged()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-zip-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "backup.zip");
        var aiBytes = GenerativeAssetPersistenceTests.SamplePngBytes;

        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var json = zip.CreateEntry("data.json");
                using (var w = new StreamWriter(json.Open()))
                    w.Write("""[{"name":"ZipSave","date":"2026-06-01","shots":[]}]""");
            }

            var project = new CaveProjectDocument { Name = "ZipSave", Date = "2026-06-01", Shots = [] };
            ProjectAiAssetPersistence.AttachSessionAssets(project, zipPath, aiBytes, aiBytes);

            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = [project],
                PrimarySourcePath = zipPath,
            });

            using var reread = ZipFile.OpenRead(zipPath);
            Assert.IsNotNull(reread.GetEntry("export_assets/windows/ZipSave/ai_cartography.png"));
            Assert.IsNotNull(reread.GetEntry("export_assets/windows/ZipSave/structure_mask.png"));

            var jsonText = new StreamReader(reread.GetEntry("data.json")!.Open()).ReadToEnd();
            Assert.IsTrue(jsonText.Contains(ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey, StringComparison.Ordinal));
            Assert.IsTrue(jsonText.Contains(ProjectAiAssetPersistence.AiStructureMaskLocalPathKey, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void ResolveAssetsForZipSave_reads_mask_from_session_cache()
    {
        var project = new CaveProjectDocument { Name = "Sess", Date = "2026-06-01" };
        var ai = GenerativeAssetPersistenceTests.SamplePngBytes;
        GenerativeMapSessionCache.Set(project, ai, ai);

        var (resolvedAi, resolvedMask) = ProjectAiAssetPersistence.ResolveAssetsForZipSave(
            project,
            "dummy.zip",
            (null, null));

        Assert.IsNotNull(resolvedAi);
        Assert.IsNotNull(resolvedMask);
        GenerativeMapSessionCache.Clear(project);
    }
}
