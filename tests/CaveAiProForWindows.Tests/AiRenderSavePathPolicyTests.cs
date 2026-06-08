using System.IO;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AiRenderSavePathPolicyTests
{
    [TestMethod]
    public void IsRestrictedWriteLocation_detects_INetCache()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "INetCache",
            "Content.Outlook",
            "survey.zip");

        Assert.IsTrue(AiRenderSavePathPolicy.IsRestrictedWriteLocation(path));
    }

    [TestMethod]
    public void CanWriteBesideSourceFile_returns_true_for_temp_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "caveai-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "survey.json");
        File.WriteAllText(jsonPath, "[]");

        try
        {
            Assert.IsTrue(AiRenderSavePathPolicy.CanWriteBesideSourceFile(jsonPath));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateZipSaveStagingPath_uses_temp_not_source_directory()
    {
        var staging = AiRenderSavePathPolicy.CreateZipSaveStagingPath();

        Assert.IsTrue(staging.Contains("CaveAiProWindows", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(staging.Contains("zip-save", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(staging.Contains("INetCache", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AttachSessionAssets_uses_ai_renders_folder_for_restricted_source()
    {
        var restrictedDir = Path.Combine(
            Path.GetTempPath(),
            "caveai-restricted-" + Guid.NewGuid().ToString("N"),
            "INetCache");
        Directory.CreateDirectory(restrictedDir);
        var jsonPath = Path.Combine(restrictedDir, "survey.json");
        File.WriteAllText(jsonPath, "[]");

        var samplePng = GenerativeAssetPersistenceTests.SamplePngBytes;

        try
        {
            var project = new CaveProjectDocument { Name = "CacheCave", Date = "2026-06-07" };
            ProjectAiAssetPersistence.AttachSessionAssets(project, jsonPath, samplePng, samplePng);

            var aiRel = project.ExtensionData![ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey].GetString();
            Assert.IsNotNull(aiRel);
            Assert.IsTrue(Path.IsPathRooted(aiRel));
            Assert.IsTrue(aiRel!.Contains("ai-renders", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(File.Exists(aiRel));
            Assert.IsFalse(aiRel.Contains("INetCache", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(restrictedDir)!))
                Directory.Delete(Path.GetDirectoryName(restrictedDir)!, recursive: true);
        }
    }
}
