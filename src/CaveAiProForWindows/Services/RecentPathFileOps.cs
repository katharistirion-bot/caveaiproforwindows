using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>Rename / delete paths shown in the Recent files list; updates persist via <see cref="RecentPathsStore"/>.</summary>
public static class RecentPathFileOps
{
    public static bool Exists(string path) =>
        !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));

    public static bool IsDirectory(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public static bool TryRename(string path, string newName, out string? newFullPath, out string? error)
    {
        newFullPath = null;
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !Exists(path))
        {
            error = "Path not found.";
            return false;
        }

        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "Enter a name.";
            return false;
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "The name contains invalid characters.";
            return false;
        }

        var parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            error = "Cannot rename this path.";
            return false;
        }

        newFullPath = Path.Combine(parent, trimmed);
        if (string.Equals(path, newFullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (File.Exists(newFullPath) || Directory.Exists(newFullPath))
        {
            error = "A file or folder with that name already exists.";
            return false;
        }

        try
        {
            if (File.Exists(path))
                File.Move(path, newFullPath);
            else
                Directory.Move(path, newFullPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            newFullPath = null;
            return false;
        }
    }

    public static bool TryDelete(string path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path) || !Exists(path))
        {
            error = "Path not found.";
            return false;
        }

        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
