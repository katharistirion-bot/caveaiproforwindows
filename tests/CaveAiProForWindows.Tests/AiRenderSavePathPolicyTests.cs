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
    public void AttachSessionAssets_is_no_op_after_generative_removal()
    {
        var jsonPath = Path.Combine(Path.GetTempPath(), "caveai-noop-" + Guid.NewGuid().ToString("N"), "survey.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, "[]");

        try
        {
            var project = new CaveProjectDocument { Name = "NoAiCave", Date = "2026-06-07" };
            ProjectAiAssetPersistence.AttachSessionAssets(project, jsonPath, [1, 2, 3], [4, 5, 6]);

            Assert.IsNull(ProjectAiAssetPersistence.TryReadAiMapRelativePath(project));
            Assert.IsNull(ProjectAiAssetPersistence.TryReadStructureMaskRelativePath(project));
        }
        finally
        {
            var dir = Path.GetDirectoryName(jsonPath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
