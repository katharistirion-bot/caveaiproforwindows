using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Writes an Android-compatible single-project backup ZIP (<c>data.json</c> + manifests + <c>export_assets/</c>).
/// </summary>
public static class AndroidBackupZipExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static void ExportSingleProject(
        CaveProjectDocument project,
        string zipPath,
        string? sourceZipPath = null,
        Action<CaveProjectDocument>? beforeSerialize = null,
        IReadOnlyList<KnownCaveRecord>? libraryCards = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("ZIP path is required.", nameof(zipPath));

        beforeSerialize?.Invoke(project);
        DesignLayerVectorLinesSerializer.SyncWindowsInkToVectorLines(project);
        CaveProjectJsonWriteNormalizer.PrepareAll(new[] { project });

        var jsonBytes = Utf8Bom.GetBytes(SurveyPortableZipExporter.SerializeSingleProjectArray(project));
        var staged = ProjectAiAssetPersistence.TakeStagedBytes(project);
        var (ai, mask) = ProjectAiAssetPersistence.ResolveAssetsForZipSave(project, sourceZipPath ?? "", staged);
        var assetEntries = ProjectAiAssetPersistence.CollectZipAssetEntries(project, ai, mask).ToList();

        var copiedExportAssets = new List<(string Path, byte[] Bytes)>();
        if (!string.IsNullOrWhiteSpace(sourceZipPath) && File.Exists(sourceZipPath))
            copiedExportAssets.AddRange(CollectExportAssetsFromSource(sourceZipPath, project.Name));

        var mapInventoryBytes = TryCopyMapInventoryFromSource(sourceZipPath);
        var includesMapInventory = mapInventoryBytes != null;
        var bundlesLocalMedia = assetEntries.Count > 0 || copiedExportAssets.Count > 0;
        var libraryList = libraryCards?
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Name))
            .ToList() ?? [];
        var includesCaveLibrary = libraryList.Count > 0;
        var libraryBytes = includesCaveLibrary ? CaveLibraryJsonLoader.SerializeToUtf8(libraryList) : null;

        var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var stagedEntries = new List<(string Path, byte[] Bytes)>();

        void Stage(string path, byte[] bytes)
        {
            stagedEntries.Add((path, bytes));
            fileHashes[path] = BackupZipManifestWriter.Sha256Hex(bytes);
        }

        var manifestBytes = BackupZipManifestWriter.BuildBackupManifest(
            project,
            includesMapInventory,
            bundlesLocalMedia,
            includesCaveLibrary,
            libraryList.Count);
        Stage("backup_manifest.json", manifestBytes);

        var readme = Encoding.UTF8.GetBytes(
            "CAVE AI PRO — Android backup export (Windows)\r\n" +
            "- backup_manifest.json / integrity_manifest.json — same layout as CaveAI Pro Android export.\r\n" +
            "- data.json: single CaveProject in Gson-compatible JSON array.\r\n" +
            "- cave_library.json: Cave Library cards when the Windows session has them (linkedLibraryCaveId).\r\n" +
            "- export_assets/: bundled map/photo assets when available.\r\n");
        Stage("README.txt", readme);
        Stage("data.json", jsonBytes);
        if (libraryBytes != null)
            Stage(CaveLibraryJsonLoader.CaveLibraryEntryName, libraryBytes);

        foreach (var (entryPath, bytes) in assetEntries)
            Stage(entryPath, bytes);
        foreach (var (entryPath, bytes) in copiedExportAssets)
            Stage(entryPath, bytes);
        if (mapInventoryBytes != null)
            Stage("map_inventory.json", mapInventoryBytes);

        var temp = zipPath + ".tmp";
        if (File.Exists(temp))
            File.Delete(temp);

        using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var (path, bytes) in stagedEntries)
                WriteEntry(zip, path, bytes);

            var integrityBytes = BackupZipManifestWriter.BuildIntegrityManifest(fileHashes);
            WriteEntry(zip, "integrity_manifest.json", integrityBytes);
        }

        File.Move(temp, zipPath, overwrite: true);
    }

    private static byte[]? TryCopyMapInventoryFromSource(string? sourceZipPath)
    {
        if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
            return null;
        try
        {
            using var fs = File.OpenRead(sourceZipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = ZipMapInventoryReader.FindMapInventoryZipEntry(zip);
            if (entry == null)
                return null;
            using var ms = new MemoryStream();
            using var input = entry.Open();
            input.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static List<(string Path, byte[] Bytes)> CollectExportAssetsFromSource(string sourceZipPath, string projectName)
    {
        var list = new List<(string, byte[])>();
        try
        {
            using var srcFs = File.OpenRead(sourceZipPath);
            using var src = new ZipArchive(srcFs, ZipArchiveMode.Read, leaveOpen: false);
            var caveToken = SanitizeFolderToken(projectName);
            foreach (var entry in src.Entries)
            {
                if (entry.Length == 0)
                    continue;
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith("export_assets/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(caveToken) &&
                    !name.Contains(caveToken, StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("export_assets/windows/", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var input = entry.Open();
                using var ms = new MemoryStream();
                input.CopyTo(ms);
                list.Add((name, ms.ToArray()));
            }
        }
        catch
        {
            /* optional asset copy */
        }

        return list;
    }

    private static string SanitizeFolderToken(string? name)
    {
        var t = (name ?? "").Trim();
        if (t.Length == 0)
            return "";
        return string.Join("_", t.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }
}
