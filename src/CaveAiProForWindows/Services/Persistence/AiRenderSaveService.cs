using System.IO;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Saves AI render PNG assets and survey metadata after generative map rendering.
/// Uses safe directories when the open survey file is in a restricted cache location.
/// </summary>
public static class AiRenderSaveService
{
    public sealed class SaveAfterRenderRequest
    {
        public required CaveProjectDocument Project { get; init; }

        public required string PrimarySourcePath { get; init; }

        public required IReadOnlyList<CaveProjectDocument> AllProjectsInSource { get; init; }

        public required byte[] AiMapPng { get; init; }

        public byte[]? StructureMaskPng { get; init; }

        public Action<CaveProjectDocument>? BeforeSerialize { get; init; }
    }

    public sealed class ExportAfterRenderRequest
    {
        public required CaveProjectDocument Project { get; init; }

        public required string PrimarySourcePath { get; init; }

        public required IReadOnlyList<CaveProjectDocument> AllProjectsInSource { get; init; }

        public required byte[] AiMapPng { get; init; }

        public byte[]? StructureMaskPng { get; init; }

        public Action<CaveProjectDocument>? BeforeSerialize { get; init; }

        public required string DestinationPath { get; init; }
    }

    public static bool CanAutoSaveBesideSource(string? primarySourcePath) =>
        AiRenderSavePathPolicy.CanWriteBesideSourceFile(primarySourcePath);

    /// <summary>Auto-save beside the open source when that location is writable.</summary>
    public static bool TryAutoSaveAfterRender(SaveAfterRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanAutoSaveBesideSource(request.PrimarySourcePath))
            return false;

        return GenerativeAssetPersistenceService.TryPersistAfterRender(
            request.Project,
            request.PrimarySourcePath,
            request.AllProjectsInSource,
            request.AiMapPng,
            request.StructureMaskPng,
            request.BeforeSerialize);
    }

    /// <summary>Export survey + AI assets to a user-chosen writable path (SaveFileDialog result).</summary>
    public static bool TryExportAfterRender(ExportAfterRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dest = Path.GetFullPath(request.DestinationPath);
        var ext = Path.GetExtension(dest);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Export destination must be a .json or .zip file.");

        if (AiRenderSavePathPolicy.IsRestrictedWriteLocation(dest))
            throw new UnauthorizedAccessException("Cannot export AI render into a Windows system cache folder.");

        var source = Path.GetFullPath(request.PrimarySourcePath);
        if (!string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
        {
            if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(source))
                    throw new FileNotFoundException("Source survey archive not found.", source);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(source))
                    File.Copy(source, dest, overwrite: true);
                else
                {
                    var json = SurveyPortableZipExporter.SerializeProjectArray(request.AllProjectsInSource);
                    File.WriteAllText(dest, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }
            }
        }

        return GenerativeAssetPersistenceService.TryPersistAfterRender(
            request.Project,
            dest,
            request.AllProjectsInSource,
            request.AiMapPng,
            request.StructureMaskPng,
            request.BeforeSerialize);
    }

    public static string SuggestExportFileName(CaveProjectDocument project, string primarySourcePath)
    {
        var safe = string.Join("_", (project.Name ?? "cave").Split(Path.GetInvalidFileNameChars()));
        var ext = Path.GetExtension(primarySourcePath);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            ext = ".zip";
        return $"{safe}_ai_render{ext}";
    }
}
