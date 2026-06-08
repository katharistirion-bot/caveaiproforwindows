using System.IO;

using Microsoft.Win32;



namespace CaveAiProForWindows.Services;



/// <summary>

/// Release builds must run from a registered install (MSI → HKLM/HKCU, Velopack → %LocalAppData%,

/// Microsoft Store → WindowsApps, or per-user script → HKCU) so loose copies under Downloads/dist

/// cannot impersonate the product install.

/// </summary>

public static class InstallationGuard

{

    private const string RegistryRelativePath = @"SOFTWARE\CaveAiPro\CaveAiProForWindows";



    public static string BlockedUserMessage

    {

        get

        {

            var channels = DistributionChannel.IsMicrosoftStoreBuild

                ? "Install from the Microsoft Store, or use CaveAiProForWindows-*-Setup.exe from caveaipro.com or GitHub Releases (sideload — supports automatic updates), "

                  + "CaveAiProForWindows-Setup.msi, or the per-user install script."

                : "Install using CaveAiProForWindows-*-Setup.exe from caveaipro.com or GitHub Releases (recommended — supports automatic updates), "

                  + "CaveAiProForWindows-Setup.msi, or the per-user install script.";



            return "This copy of CAVE AI PRO is not running from a registered installation.\r\n\r\n"

                   + channels

                   + " Then start the app from the Start menu.\r\n\r\n"

                   + "If you are a developer, run a Debug build.";

        }

    }



    public static bool IsLaunchedFromRegisteredInstall()

    {

#if DEBUG

        return true;

#else

        var exePath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exePath))

            return false;

        var exeDir = NormalizeInstallDir(Path.GetDirectoryName(exePath));

        if (string.IsNullOrEmpty(exeDir))

            return false;



        if (IsMicrosoftStoreInstallPath(exeDir))

            return true;



        if (MatchesRegistry(RegistryHive.LocalMachine, exeDir)

            || MatchesRegistry(RegistryHive.CurrentUser, exeDir))

            return true;



        return IsVelopackInstallation(exeDir);

#endif

    }



    /// <summary>

    /// True when the executable lives under a Microsoft Store / MSIX package layout

    /// (e.g. <c>Program Files\WindowsApps\</c> or <c>%LocalAppData%\Microsoft\WindowsApps\</c>).

    /// </summary>

    internal static bool IsMicrosoftStoreInstallPath(string? exeDir)

    {

        if (string.IsNullOrWhiteSpace(exeDir))

            return false;



        var normalized = NormalizeInstallDir(exeDir);

        if (string.IsNullOrEmpty(normalized))

            return false;



        if (normalized.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))

            return true;



        if (normalized.Contains(@"\ProgramData\Packages\", StringComparison.OrdinalIgnoreCase))

            return true;



        return false;

    }



    private static bool IsVelopackInstallation(string exeDir)

    {

        if (AppUpdateService.IsVelopackInstalled())

            return true;



        if (File.Exists(Path.Combine(exeDir, "sq.version")))

            return true;



        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var velopackRoot = NormalizeInstallDir(Path.Combine(localAppData, AppUpdateService.VelopackAppId));

        if (string.IsNullOrEmpty(velopackRoot))

            return false;



        return exeDir.StartsWith(velopackRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)

               || string.Equals(exeDir, velopackRoot, StringComparison.OrdinalIgnoreCase);

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


