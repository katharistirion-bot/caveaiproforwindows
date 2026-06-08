using System.IO;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Persistence;

/// <summary>
/// Detects Windows/system cache locations and other paths where AI render assets must not be written.
/// </summary>
public static class AiRenderSavePathPolicy
{
    private static readonly string[] RestrictedPathMarkers =
    [
        $"{Path.DirectorySeparatorChar}INetCache{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}INetCache{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Temporary Internet Files{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}Temporary Internet Files{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}INetCookies{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}INetCookies{Path.AltDirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}WebView2{Path.DirectorySeparatorChar}EBWebView{Path.DirectorySeparatorChar}",
    ];

    public static string LocalAppRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows");

    public static string AiRenderWorkingRoot => Path.Combine(LocalAppRoot, "ai-renders");

    public static string ZipSaveStagingRoot =>
        Path.Combine(Path.GetTempPath(), "CaveAiProWindows", "zip-save");

    /// <summary>Desktop, then Documents, then user profile.</summary>
    public static string GetDefaultExportInitialDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Directory.Exists(docs))
            return docs;

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (Directory.Exists(desktop))
            return desktop;

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static bool IsRestrictedWriteLocation(string? fileOrDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(fileOrDirectoryPath))
            return true;

        try
        {
            var full = Path.GetFullPath(fileOrDirectoryPath.Trim());

            if (IsUnderProgramFiles(full))
                return true;

            foreach (var marker in RestrictedPathMarkers)
            {
                if (full.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (full.Contains("INetCache", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static bool CanWriteBesideSourceFile(string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
            return false;

        try
        {
            var full = Path.GetFullPath(sourceFilePath);
            if (!File.Exists(full))
                return false;

            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir))
                return false;

            if (IsRestrictedWriteLocation(dir))
                return false;

            return ProbeDirectoryWritable(dir);
        }
        catch
        {
            return false;
        }
    }

    public static string CreateZipSaveStagingPath()
    {
        Directory.CreateDirectory(ZipSaveStagingRoot);
        return Path.Combine(ZipSaveStagingRoot, Guid.NewGuid().ToString("N") + ".caveai-save.tmp");
    }

    /// <summary>
    /// Writable working folder for AI PNG assets when the survey source lives in a restricted location.
    /// </summary>
    public static string GetProjectWorkingAssetDirectory(CaveProjectDocument project, string? sourceFilePath)
    {
        var key = $"{SanitizeKeySegment(project.Name)}_{SanitizeKeySegment(project.Date)}";
        var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key + "|" + (sourceFilePath ?? ""))))[..12]
            .ToLowerInvariant();

        var root = Path.Combine(AiRenderWorkingRoot, hash);
        Directory.CreateDirectory(root);
        return root;
    }

    private static bool IsUnderProgramFiles(string fullPath)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return StartsWithDirectory(fullPath, pf) || StartsWithDirectory(fullPath, pf86);
    }

    private static bool StartsWithDirectory(string fullPath, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;

        try
        {
            var normalizedRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                 + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(fullPath);
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ProbeDirectoryWritable(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probe = Path.Combine(directoryPath, $".caveai-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string SanitizeKeySegment(string? value)
    {
        var s = string.Join(
            "_",
            (value ?? "cave").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(s) ? "cave" : s;
    }
}
