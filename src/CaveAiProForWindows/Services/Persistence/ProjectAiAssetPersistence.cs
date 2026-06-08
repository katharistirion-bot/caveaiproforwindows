using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.GenerativeMap;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Writes generative AI PNG and structure mask PNG beside the project file (or stages for ZIP embed)
/// and records relative paths on the document.
/// </summary>
public static class ProjectAiAssetPersistence
{
    public const string AiGeneratedMapLocalPathKey = "aiGeneratedMapLocalPath";
    public const string AiStructureMaskLocalPathKey = "aiStructureMaskLocalPath";

    /// <summary>Legacy Windows desktop keys — still written for backward compatibility.</summary>
    public const string LegacyAiCartographyUriKey = "windowsAiCartographyImageUri";
    public const string LegacyStructureMaskUriKey = "windowsStructureMaskImageUri";

    private static readonly string[] AiMapPathKeys =
    [
        AiGeneratedMapLocalPathKey,
        LegacyAiCartographyUriKey,
    ];

    private static readonly string[] StructureMaskPathKeys =
    [
        AiStructureMaskLocalPathKey,
        LegacyStructureMaskUriKey,
    ];

    private static readonly Dictionary<string, (byte[]? Ai, byte[]? Mask)> StagedBytes =
        new(StringComparer.OrdinalIgnoreCase);

    public static void StageBytesForZipEmbed(CaveProjectDocument project, byte[]? aiCartographyPng, byte[]? structureMaskPng)
    {
        StagedBytes[StageKey(project)] = (aiCartographyPng, structureMaskPng);
    }

    public static (byte[]? Ai, byte[]? Mask) TakeStagedBytes(CaveProjectDocument project)
    {
        var key = StageKey(project);
        if (!StagedBytes.TryGetValue(key, out var pair))
            return (null, null);
        StagedBytes.Remove(key);
        return pair;
    }

    private static string StageKey(CaveProjectDocument project) =>
        $"{project.Name}|{project.Date}|{project.Shots?.Count ?? 0}";

    /// <summary>
    /// Saves PNG bytes under <c>export_assets/windows/{cave}/</c> next to the source file and updates extension fields.
    /// </summary>
    public static void AttachSessionAssets(
        CaveProjectDocument project,
        string sourceFilePath,
        byte[]? aiCartographyPng,
        byte[]? structureMaskPng)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            return;

        var writeBesideSource = AiRenderSavePathPolicy.CanWriteBesideSourceFile(sourceFilePath);
        var baseDir = writeBesideSource
            ? Path.GetDirectoryName(Path.GetFullPath(sourceFilePath))
            : AiRenderSavePathPolicy.GetProjectWorkingAssetDirectory(project, sourceFilePath);
        if (string.IsNullOrEmpty(baseDir))
            return;

        var caveFolder = SanitizeFolderName(project.Name);
        var assetRoot = writeBesideSource
            ? Path.Combine(baseDir, "export_assets", "windows", caveFolder)
            : baseDir;
        Directory.CreateDirectory(assetRoot);

        project.ExtensionData ??= new Dictionary<string, JsonElement>();

        if (aiCartographyPng is { Length: > 0 })
        {
            const string fileName = "ai_cartography.png";
            var diskPath = Path.Combine(assetRoot, fileName);
            File.WriteAllBytes(diskPath, aiCartographyPng);
            var rel = writeBesideSource
                ? ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, fileName))
                : ToForwardSlash(diskPath);
            WritePathMetadata(project, AiGeneratedMapLocalPathKey, LegacyAiCartographyUriKey, rel);
        }

        if (structureMaskPng is { Length: > 0 })
        {
            const string fileName = "structure_mask.png";
            var diskPath = Path.Combine(assetRoot, fileName);
            File.WriteAllBytes(diskPath, structureMaskPng);
            var rel = writeBesideSource
                ? ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, fileName))
                : ToForwardSlash(diskPath);
            WritePathMetadata(project, AiStructureMaskLocalPathKey, LegacyStructureMaskUriKey, rel);
        }

        StageBytesForZipEmbed(project, aiCartographyPng, structureMaskPng);
        DesignLayerMapObjectsSerializer.TouchWindowsEditMetadata(project);
    }

    public static string? TryReadAiMapRelativePath(CaveProjectDocument project) =>
        TryReadRelativePath(project, AiMapPathKeys);

    public static string? TryReadStructureMaskRelativePath(CaveProjectDocument project) =>
        TryReadRelativePath(project, StructureMaskPathKeys);

    /// <summary>Loads asset bytes from disk beside the source file or from inside a ZIP archive.</summary>
    public static byte[]? TryLoadAssetBytes(string sourceFilePath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalizedRel = relativePath.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            return File.ReadAllBytes(relativePath);

        var sourceFull = Path.GetFullPath(sourceFilePath);

        if (sourceFull.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var fromZip = TryReadZipEntryBytes(sourceFull, normalizedRel);
            if (fromZip is { Length: > 0 })
                return fromZip;
        }

        var baseDir = Path.GetDirectoryName(sourceFull);
        if (string.IsNullOrEmpty(baseDir))
            return null;

        var diskPath = Path.Combine(baseDir, normalizedRel.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(diskPath) ? File.ReadAllBytes(diskPath) : null;
    }

    private static byte[]? TryReadZipEntryBytes(string zipPath, string slashPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(slashPath)
                        ?? zip.Entries.FirstOrDefault(e =>
                            e.FullName.Equals(slashPath, StringComparison.OrdinalIgnoreCase)
                            || ExplorationDataLoader.NormalizeZipEntryPath(e.FullName)
                                .Equals(slashPath, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                return null;
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadRelativePath(CaveProjectDocument project, IReadOnlyList<string> keys)
    {
        if (project.ExtensionData == null)
            return null;

        foreach (var key in keys)
        {
            if (!project.ExtensionData.TryGetValue(key, out var el))
                continue;
            var path = el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() : null;
            if (!string.IsNullOrWhiteSpace(path))
                return path.Replace('\\', '/');
        }

        return null;
    }

    private static void WritePathMetadata(
        CaveProjectDocument project,
        string primaryKey,
        string legacyKey,
        string relativePath)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(relativePath));
        var cloned = doc.RootElement.Clone();
        project.ExtensionData![primaryKey] = cloned;
        project.ExtensionData[legacyKey] = cloned.Clone();
    }

    /// <summary>
    /// Resolves AI PNG bytes for a ZIP save: staged embed, session cache, then on-disk / existing ZIP entry
    /// referenced in <see cref="CaveProjectDocument.ExtensionData"/>.
    /// </summary>
    public static (byte[]? Ai, byte[]? Mask) ResolveAssetsForZipSave(
        CaveProjectDocument project,
        string zipPath,
        (byte[]? Ai, byte[]? Mask) staged)
    {
        var session = GenerativeMapSessionCache.TryGet(project);
        var ai = staged.Ai ?? session?.PngBytes;
        var mask = staged.Mask ?? session?.StructureMaskPng;

        if (ai is not { Length: > 0 })
        {
            var aiPath = TryReadAiMapRelativePath(project);
            if (!string.IsNullOrWhiteSpace(aiPath))
                ai = TryLoadAssetBytes(zipPath, aiPath);
        }

        if (mask is not { Length: > 0 })
        {
            var maskPath = TryReadStructureMaskRelativePath(project);
            if (!string.IsNullOrWhiteSpace(maskPath))
                mask = TryLoadAssetBytes(zipPath, maskPath);
        }

        EnsureExtensionPathsForZipEmbed(project, ai, mask);
        return (ai, mask);
    }

    /// <summary>Writes canonical relative paths into ExtensionData when embedding PNGs into a ZIP.</summary>
    public static void EnsureExtensionPathsForZipEmbed(
        CaveProjectDocument project,
        byte[]? aiCartographyPng,
        byte[]? structureMaskPng)
    {
        if (aiCartographyPng is not { Length: > 0 } && structureMaskPng is not { Length: > 0 })
            return;

        project.ExtensionData ??= new Dictionary<string, JsonElement>();
        var caveFolder = SanitizeFolderName(project.Name);

        if (aiCartographyPng is { Length: > 0 })
        {
            var rel = ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, "ai_cartography.png"));
            WritePathMetadata(project, AiGeneratedMapLocalPathKey, LegacyAiCartographyUriKey, rel);
        }

        if (structureMaskPng is { Length: > 0 })
        {
            var rel = ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, "structure_mask.png"));
            WritePathMetadata(project, AiStructureMaskLocalPathKey, LegacyStructureMaskUriKey, rel);
        }
    }

    /// <summary>When saving into a ZIP, returns zip entry paths for assets that should be embedded.</summary>
    public static IReadOnlyList<(string ZipEntryPath, byte[] Bytes)> CollectZipAssetEntries(
        CaveProjectDocument project,
        byte[]? aiCartographyPng,
        byte[]? structureMaskPng)
    {
        var list = new List<(string, byte[])>();
        var caveFolder = SanitizeFolderName(project.Name);

        if (aiCartographyPng is { Length: > 0 })
        {
            var entry = ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, "ai_cartography.png"));
            list.Add((entry, aiCartographyPng));
        }

        if (structureMaskPng is { Length: > 0 })
        {
            var entry = ToForwardSlash(Path.Combine("export_assets", "windows", caveFolder, "structure_mask.png"));
            list.Add((entry, structureMaskPng));
        }

        return list;
    }

    private static string SanitizeFolderName(string? name)
    {
        var s = string.Join("_", (name ?? "cave").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(s) ? "cave" : s;
    }

    private static string ToForwardSlash(string path) => path.Replace('\\', '/');
}
