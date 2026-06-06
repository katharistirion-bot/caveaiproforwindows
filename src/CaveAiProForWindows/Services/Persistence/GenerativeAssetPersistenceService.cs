using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.GenerativeMap;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Persists generative AI map + structure mask PNGs, hydrates <see cref="GenerativeMapSessionCache"/> on open,
/// and auto-saves project metadata after a successful render.
/// </summary>
public static class GenerativeAssetPersistenceService
{
    /// <summary>Writes PNG files, updates <see cref="CaveProjectDocument.ExtensionData"/>, and saves the source archive.</summary>
    public static bool TryPersistAfterRender(
        CaveProjectDocument project,
        string primarySourcePath,
        IReadOnlyList<CaveProjectDocument> allProjectsInSource,
        byte[] aiMapPng,
        byte[]? structureMaskPng,
        Action<CaveProjectDocument>? beforeSerialize = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(primarySourcePath) || !File.Exists(primarySourcePath))
            return false;
        if (aiMapPng is not { Length: > 0 })
            return false;

        var ext = Path.GetExtension(primarySourcePath);
        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        ProjectAiAssetPersistence.AttachSessionAssets(project, primarySourcePath, aiMapPng, structureMaskPng);

        ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
        {
            Projects = allProjectsInSource.ToList(),
            PrimarySourcePath = primarySourcePath,
            BeforeSerialize = beforeSerialize,
        });

        return true;
    }

    /// <summary>
    /// Loads persisted AI assets referenced in <paramref name="project"/> into the in-memory session cache
    /// (Sketch Editor + X-Ray overlay) without calling Replicate.
    /// </summary>
    public static bool TryHydrateSessionFromProject(CaveProjectDocument project, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(sourcePath))
            return false;

        if (GenerativeMapSessionCache.TryGet(project) != null)
            return true;

        var aiPath = ProjectAiAssetPersistence.TryReadAiMapRelativePath(project);
        if (string.IsNullOrWhiteSpace(aiPath))
            return false;

        var aiBytes = ProjectAiAssetPersistence.TryLoadAssetBytes(sourcePath, aiPath);
        if (aiBytes is not { Length: > 0 })
            return false;

        byte[]? maskBytes = null;
        var maskPath = ProjectAiAssetPersistence.TryReadStructureMaskRelativePath(project);
        if (!string.IsNullOrWhiteSpace(maskPath))
            maskBytes = ProjectAiAssetPersistence.TryLoadAssetBytes(sourcePath, maskPath);

        GenerativeMapSessionCache.Set(project, aiBytes, maskBytes);
        return true;
    }

    /// <summary>Hydrates every project that references on-disk generative assets.</summary>
    public static int HydrateAll(IEnumerable<CaveProjectDocument> projects)
    {
        var count = 0;
        foreach (var p in projects)
        {
            var source = p.LoadedFromFile;
            if (string.IsNullOrWhiteSpace(source))
                continue;
            if (TryHydrateSessionFromProject(p, source))
                count++;
        }

        return count;
    }

    /// <summary>Serializes extension metadata keys for tests and export pipelines.</summary>
    public static IReadOnlyDictionary<string, string> ReadPersistedPathMetadata(CaveProjectDocument project)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var ai = ProjectAiAssetPersistence.TryReadAiMapRelativePath(project);
        if (!string.IsNullOrWhiteSpace(ai))
            map[ProjectAiAssetPersistence.AiGeneratedMapLocalPathKey] = ai;
        var mask = ProjectAiAssetPersistence.TryReadStructureMaskRelativePath(project);
        if (!string.IsNullOrWhiteSpace(mask))
            map[ProjectAiAssetPersistence.AiStructureMaskLocalPathKey] = mask;
        return map;
    }
}
