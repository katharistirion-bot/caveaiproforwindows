using System.IO;
using System.IO.Compression;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Writes an Android-compatible single-project backup ZIP (<c>data.json</c> + <c>export_assets/</c>).
/// </summary>
public static class AndroidBackupZipExporter
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static void ExportSingleProject(
        CaveProjectDocument project,
        string zipPath,
        string? sourceZipPath = null,
        Action<CaveProjectDocument>? beforeSerialize = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("ZIP path is required.", nameof(zipPath));

        beforeSerialize?.Invoke(project);
        CaveProjectJsonWriteNormalizer.PrepareAll(new[] { project });

        var jsonBytes = Utf8Bom.GetBytes(SurveyPortableZipExporter.SerializeSingleProjectArray(project));
        var temp = zipPath + ".tmp";
        if (File.Exists(temp))
            File.Delete(temp);

        using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
        {
            WriteEntry(zip, "data.json", jsonBytes);

            var staged = ProjectAiAssetPersistence.TakeStagedBytes(project);
            var (ai, mask) = ProjectAiAssetPersistence.ResolveAssetsForZipSave(project, sourceZipPath ?? "", staged);
            foreach (var (entryPath, bytes) in ProjectAiAssetPersistence.CollectZipAssetEntries(project, ai, mask))
                WriteEntry(zip, entryPath, bytes);

            if (!string.IsNullOrWhiteSpace(sourceZipPath) && File.Exists(sourceZipPath))
                CopyExportAssetsFromSource(zip, sourceZipPath, project.Name);

            var readme = Encoding.UTF8.GetBytes(
                "CAVE AI PRO — Android backup export (Windows)\r\n" +
                "- data.json: single CaveProject in Gson-compatible JSON array.\r\n" +
                "- export_assets/: bundled map/photo assets when available.\r\n");
            WriteEntry(zip, "README.txt", readme);
        }

        File.Move(temp, zipPath, overwrite: true);
    }

    private static void CopyExportAssetsFromSource(ZipArchive target, string sourceZipPath, string projectName)
    {
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
                WriteEntry(target, name, ms.ToArray());
            }
        }
        catch
        {
            /* optional asset copy */
        }
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
