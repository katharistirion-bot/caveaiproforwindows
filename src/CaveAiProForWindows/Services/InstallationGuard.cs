using System.IO;
using Microsoft.Win32;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Release builds must run from a registered install (MSI → HKLM, or per-user script → HKCU)
/// so loose copies under Downloads/dist cannot impersonate the product install.
/// </summary>
public static class InstallationGuard
{
    private const string RegistryRelativePath = @"SOFTWARE\CaveAiPro\CaveAiProForWindows";
    private const string EnvSkip = "CAVEAI_DEV_SKIP_INSTALL_CHECK";

    public static string BlockedUserMessage =>
        "This copy of CAVE AI PRO is not running from a registered installation.\r\n\r\n"
        + "Install using CaveAiProForWindows-Setup.msi (recommended), then start the app from the Start menu "
        + "or from Program Files.\r\n\r\n"
        + "If you are a developer, run a Debug build, or set environment variable "
        + EnvSkip + "=1 for this process only.";

    public static bool IsLaunchedFromRegisteredInstall()
    {
#if DEBUG
        return true;
#else
        if (string.Equals(Environment.GetEnvironmentVariable(EnvSkip), "1", StringComparison.Ordinal))
            return true;

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;
        var exeDir = NormalizeInstallDir(Path.GetDirectoryName(exePath));
        if (string.IsNullOrEmpty(exeDir))
            return false;

        return MatchesRegistry(RegistryHive.LocalMachine, exeDir)
               || MatchesRegistry(RegistryHive.CurrentUser, exeDir);
#endif
    }

    private static bool MatchesRegistry(RegistryHive hive, string exeDir)
    {
        try
        {
            using var key = hive == RegistryHive.LocalMachine
                ? Registry.LocalMachine.OpenSubKey(RegistryRelativePath)
                : Registry.CurrentUser.OpenSubKey(RegistryRelativePath);
            if (key == null)
                return false;
            if (!IsInstalledFlag(key.GetValue("Installed")))
                return false;
            var dir = key.GetValue("InstallDir") as string;
            if (!string.IsNullOrWhiteSpace(dir))
            {
                var regDir = NormalizeInstallDir(dir);
                return string.Equals(exeDir, regDir, StringComparison.OrdinalIgnoreCase);
            }

            // Older MSI builds that only wrote Installed=1 (no InstallDir): default per-machine folder.
            if (hive != RegistryHive.LocalMachine)
                return false;
            var pf = NormalizeInstallDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var legacyRoot = Path.Combine(pf, "CaveAiPro", "CaveAiProForWindows");
            return string.Equals(exeDir, legacyRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInstalledFlag(object? value) =>
        value switch
        {
            int x => x == 1,
            uint x => x == 1,
            long x => x == 1,
            byte x => x == 1,
            _ => false,
        };

    private static string NormalizeInstallDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            var full = Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return full;
        }
        catch
        {
            return "";
        }
    }
}
