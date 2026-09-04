using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidBackupZipExporterTests
{
    [TestMethod]
    public void ExportSingleProject_writes_manifest_and_integrity_matching_Android_layout()
    {
        var zip = Path.Combine(Path.GetTempPath(), "caveai_win_export_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var project = new CaveProjectDocument
            {
                Name = "RoundTrip Cave",
                Date = "2026-06-10",
                Alt = 500,
                Shots =
                [
                    new ShotRecord
                    {
                        FromStation = "A",
                        ToStation = "B",
                        Distance = 5f,
                        Azimuth = 90f,
                        Clino = 0f,
                        L = 1f,
                        R = 1.2f,
                        U = 0.8f,
                        D = 0.9f,
                    },
                ],
                MapObjects = JsonDocument.Parse(
                    """
                    [{"kind":"stroke","viewMode":0,"closed":false,"points":[[0,0],[2,0],[2,2]],"sourceClient":"CaveAiProForWindows"}]
                    """).RootElement.Clone(),
            };

            AndroidBackupZipExporter.ExportSingleProject(project, zip);

            using var fs = File.OpenRead(zip);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

            Assert.IsNotNull(archive.GetEntry("backup_manifest.json"));
            Assert.IsNotNull(archive.GetEntry("integrity_manifest.json"));
            Assert.IsNotNull(archive.GetEntry("data.json"));
            Assert.IsNotNull(archive.GetEntry("README.txt"));

            var report = IntegrityVerifier.VerifyZip(zip);
            Assert.IsNull(report.SkippedReason, report.SkippedReason ?? "");
            Assert.IsTrue(report.IsCompleteSuccess, string.Join("; ", report.Mismatches));
            Assert.IsTrue(report.FilesChecked >= 3);

            using var manifestDoc = JsonDocument.Parse(ReadEntryText(archive, "backup_manifest.json"));
            var root = manifestDoc.RootElement;
            Assert.AreEqual(2, root.GetProperty("backup_version").GetInt32());
            Assert.AreEqual("windows_pc_export", root.GetProperty("mode").GetString());
            StringAssert.Contains(root.GetProperty("app").GetString(), "Windows");
            Assert.IsTrue(root.GetProperty("edit_provenance").GetProperty("editor").GetString()?.Contains("Windows") == true);
            Assert.IsFalse(root.GetProperty("includes_cave_library").GetBoolean());

            using var dataDoc = JsonDocument.Parse(ReadEntryText(archive, "data.json"));
            var projectEl = dataDoc.RootElement[0];
            Assert.IsTrue(projectEl.TryGetProperty("vectorLines", out var vl) && vl.GetArrayLength() == 1);
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    [TestMethod]
    public void ExportSingleProject_writes_cave_library_when_cards_provided()
    {
        var zip = Path.Combine(Path.GetTempPath(), "caveai_win_lib_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var project = new CaveProjectDocument
            {
                Name = "Library Cave",
                Date = "03/09/2026",
                LinkedLibraryCaveId = "lib-1",
                SurveyArchiveSchemaVersion = "2",
            };
            var card = new KnownCaveRecord
            {
                Id = "lib-1",
                Name = "Library Cave",
                Lat = 38.22,
                Lon = 20.62,
                Type = "CAVE",
                Area = "Greece",
            };

            AndroidBackupZipExporter.ExportSingleProject(project, zip, libraryCards: new[] { card });

            using var fs = File.OpenRead(zip);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            Assert.IsNotNull(archive.GetEntry("cave_library.json"));

            var loaded = CaveLibraryJsonLoader.TryLoadFromZip(zip);
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("lib-1", loaded[0].Id);
            Assert.AreEqual("Library Cave", loaded[0].Name);
            Assert.AreEqual(38.22, loaded[0].Lat, 1e-6);

            using var dataDoc = JsonDocument.Parse(ReadEntryText(archive, "data.json"));
            var ver = dataDoc.RootElement[0].GetProperty("surveyArchiveSchemaVersion");
            Assert.AreEqual(JsonValueKind.Number, ver.ValueKind, "Android Gson expects a JSON number for surveyArchiveSchemaVersion");
            Assert.AreEqual(2, ver.GetInt32());

            using var manifestDoc = JsonDocument.Parse(ReadEntryText(archive, "backup_manifest.json"));
            Assert.IsTrue(manifestDoc.RootElement.GetProperty("includes_cave_library").GetBoolean());
            Assert.AreEqual(1, manifestDoc.RootElement.GetProperty("known_cave_count").GetInt32());

            var report = IntegrityVerifier.VerifyZip(zip);
            Assert.IsTrue(report.IsCompleteSuccess, string.Join("; ", report.Mismatches));
        }
        finally
        {
            try { File.Delete(zip); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void SerializeProject_writes_surveyArchiveSchemaVersion_as_number()
    {
        var project = new CaveProjectDocument
        {
            Name = "Schema",
            SurveyArchiveSchemaVersion = "2",
        };
        var json = SurveyPortableZipExporter.SerializeSingleProjectArray(project);
        using var doc = JsonDocument.Parse(json);
        var ver = doc.RootElement[0].GetProperty("surveyArchiveSchemaVersion");
        Assert.AreEqual(JsonValueKind.Number, ver.ValueKind);
        Assert.AreEqual(2, ver.GetInt32());
    }

    private static string ReadEntryText(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new AssertFailedException($"Missing entry {name}");
        using var sr = new StreamReader(entry.Open());
        return sr.ReadToEnd();
    }
}
