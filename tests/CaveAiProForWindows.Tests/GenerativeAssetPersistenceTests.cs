using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class GenerativeAssetPersistenceTests
{
    internal static readonly byte[] SamplePngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [TestMethod]
    public void AttachSessionAssets_writes_canonical_and_legacy_extension_paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "survey.json");
        File.WriteAllText(jsonPath, "[]");

        try
        {
            var project = new CaveProjectDocument { Name = "TestCave", Date = "2026-01-01" };
            ProjectAiAssetPersistence.AttachSessionAssets(project, jsonPath, SamplePngBytes, SamplePngBytes);

            Assert.IsTrue(project.ExtensionData!.ContainsKey(ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey));
            Assert.IsTrue(project.ExtensionData.ContainsKey(ProjectAiAssetPersistence.AiStructureMaskLocalPathKey));
            Assert.IsTrue(project.ExtensionData.ContainsKey(ProjectAiAssetPersistence.LegacyAiCartographyUriKey));

            var aiRel = project.ExtensionData[ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey].GetString();
            Assert.IsNotNull(aiRel);
            Assert.IsTrue(aiRel!.Contains("export_assets/windows/TestCave/ai_cartography.png", StringComparison.Ordinal));

            var diskPath = Path.Combine(dir, aiRel!.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(diskPath));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void TryHydrateSessionFromProject_loads_assets_into_session_cache()
    {
        GenerativeMapSessionCache.Clear(new CaveProjectDocument { Name = "__clear__", Date = "x" });

        var dir = Path.Combine(Path.GetTempPath(), "caveai-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "survey.json");
        File.WriteAllText(jsonPath, "[]");

        try
        {
            var project = new CaveProjectDocument { Name = "HydrateCave", Date = "2026-06-01" };
            ProjectAiAssetPersistence.AttachSessionAssets(project, jsonPath, SamplePngBytes, SamplePngBytes);

            var reloaded = new CaveProjectDocument
            {
                Name = project.Name,
                Date = project.Date,
                ExtensionData = project.ExtensionData,
            };

            Assert.IsTrue(GenerativeAssetPersistenceService.TryHydrateSessionFromProject(reloaded, jsonPath));
            var entry = GenerativeMapSessionCache.TryGet(reloaded);
            Assert.IsNotNull(entry);
            Assert.AreEqual(SamplePngBytes.Length, entry!.PngBytes.Length);
            Assert.IsNotNull(entry.StructureMaskPng);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void TryPersistAfterRender_updates_json_with_metadata_and_assets_on_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "survey.json");
        var project = new CaveProjectDocument { Name = "SaveCave", Date = "2026-06-01", Shots = [] };
        File.WriteAllText(jsonPath, """[{"name":"SaveCave","date":"2026-06-01","shots":[]}]""");

        try
        {
            Assert.IsTrue(GenerativeAssetPersistenceService.TryPersistAfterRender(
                project,
                jsonPath,
                [project],
                SamplePngBytes,
                SamplePngBytes));

            var text = File.ReadAllText(jsonPath);
            Assert.IsTrue(text.Contains(ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey, StringComparison.Ordinal));
            Assert.IsTrue(text.Contains(ProjectAiAssetPersistence.AiStructureMaskLocalPathKey, StringComparison.Ordinal));

            var reloaded = ExplorationDataLoader.LoadFromJsonFile(jsonPath);
            Assert.AreEqual(1, reloaded.Count);
            var meta = GenerativeAssetPersistenceService.ReadPersistedPathMetadata(reloaded[0]);
            Assert.IsTrue(meta.ContainsKey(ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey));
            Assert.IsTrue(meta.ContainsKey(ProjectAiAssetPersistence.AiStructureMaskLocalPathKey));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void TryLoadAssetBytes_reads_from_zip_entry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-gen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, "backup.zip");

        try
        {
            var project = new CaveProjectDocument { Name = "ZipCave", Date = "2026-06-01" };
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var jsonEntry = zip.CreateEntry("data.json");
                using (var w = new StreamWriter(jsonEntry.Open()))
                    w.Write("""[{"name":"ZipCave","date":"2026-06-01","shots":[]}]""");

                var assetEntry = zip.CreateEntry("export_assets/windows/ZipCave/ai_cartography.png");
                using (var s = assetEntry.Open())
                    s.Write(SamplePngBytes, 0, SamplePngBytes.Length);
            }

            project.ExtensionData = new Dictionary<string, JsonElement>
            {
                [ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey] =
                    JsonSerializer.SerializeToElement("export_assets/windows/ZipCave/ai_cartography.png"),
            };

            var bytes = ProjectAiAssetPersistence.TryLoadAssetBytes(zipPath,
                "export_assets/windows/ZipCave/ai_cartography.png");
            Assert.IsNotNull(bytes);
            Assert.AreEqual(SamplePngBytes.Length, bytes!.Length);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
