using System.Diagnostics;
using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>Local diagnostic log folder under %LOCALAPPDATA%.</summary>
public static class DiagnosticLogPaths
{
    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows");

    public static string StartupLogPath => Path.Combine(AppDataDirectory, "startup.log");

    public static string LastErrorPath => Path.Combine(AppDataDirectory, "last-error.txt");

    public static bool TryOpenFolder()
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataDirectory,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
