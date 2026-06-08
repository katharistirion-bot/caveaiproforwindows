using System.IO;
using System.IO.Compression;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>Persists in-memory <see cref="CaveProjectDocument"/> instances back to the primary .json or .zip source.</summary>
public static class ProjectPersistenceService
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public sealed class SaveRequest
    {
        public required IReadOnlyList<CaveProjectDocument> Projects { get; init; }

        public required string PrimarySourcePath { get; init; }

        /// <summary>Flush sketch / AI metadata onto each project before serialization.</summary>
        public Action<CaveProjectDocument>? BeforeSerialize { get; init; }
    }

    public static void Save(SaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Projects.Count == 0)
            throw new InvalidOperationException("No projects to save.");
        if (string.IsNullOrWhiteSpace(request.PrimarySourcePath))
            throw new InvalidOperationException("Primary source path is required.");
        if (!File.Exists(request.PrimarySourcePath))
            throw new FileNotFoundException("Source file not found.", request.PrimarySourcePath);

        foreach (var p in request.Projects)
            request.BeforeSerialize?.Invoke(p);

        CaveProjectJsonWriteNormalizer.PrepareAll(request.Projects);

        var ext = Path.GetExtension(request.PrimarySourcePath);
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            SaveZip(request);
        else if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            SaveJson(request);
        else
            throw new NotSupportedException($"Saving is supported for .json and .zip only (got {ext}).");
    }

    private static void SaveJson(SaveRequest request)
    {
        var json = SurveyPortableZipExporter.SerializeProjectArray(request.Projects);
        File.WriteAllText(request.PrimarySourcePath, json, Utf8Bom);
    }

    private static void SaveZip(SaveRequest request)
    {
        var zipPath = Path.GetFullPath(request.PrimarySourcePath);
        var tempPath = AiRenderSavePathPolicy.CreateZipSaveStagingPath();
        File.Copy(zipPath, tempPath, overwrite: true);

        try
        {
            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                RemoveEntry(zip, "data.json");
                RemoveEntry(zip, "DATA.JSON");

                var jsonBytes = Utf8Bom.GetBytes(SurveyPortableZipExporter.SerializeProjectArray(request.Projects));
                WriteEntryBytes(zip, "data.json", jsonBytes);

                foreach (var project in request.Projects)
                {
                    var staged = ProjectAiAssetPersistence.TakeStagedBytes(project);
                    var (ai, mask) = ProjectAiAssetPersistence.ResolveAssetsForZipSave(project, zipPath, staged);

                    foreach (var (entryPath, bytes) in ProjectAiAssetPersistence.CollectZipAssetEntries(project, ai, mask))
                    {
                        RemoveEntry(zip, entryPath);
                        WriteEntryBytes(zip, entryPath, bytes);
                    }
                }
            }

            File.Move(tempPath, zipPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    private static void RemoveEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? zip.Entries.FirstOrDefault(e =>
            e.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        entry?.Delete();
    }

    private static void WriteEntryBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }
}
