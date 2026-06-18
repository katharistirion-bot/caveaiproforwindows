using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Auth;

namespace CaveAiProForWindows.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var file = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        var ver = asm.GetName().Version?.ToString() ?? "?";
        VersionBlock.Text = !string.IsNullOrWhiteSpace(info) && info != ver
            ? $"{info}  (assembly {ver})"
            : $"Build {ver}" + (string.IsNullOrWhiteSpace(file) ? "" : $"  · file {file}");

        InstallBlock.Text = DistributionChannel.UpdatesHandledByStore
            ? "Installed from the Microsoft Store — updates are delivered automatically through the Store."
            : "Install from GitHub Releases using CaveAiProForWindows-*-Setup.exe for automatic updates. MSI and portable ZIP are also available.";

        var envOrigin = Environment.GetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN");
        var envOriginLine = string.IsNullOrWhiteSpace(envOrigin)
            ? "Set CAVEAIPRO_WEB_ORIGIN if the custom domain differs from the default."
            : "CAVEAIPRO_WEB_ORIGIN is set for this user/machine.";

        WebPortalBlock.Text = DistributionChannel.UpdatesHandledByStore
            ? "Updates: delivered automatically through the Microsoft Store.\n"
              + "Open the Microsoft Store app and check Library → Get updates.\n"
              + "Web portal origin: " + PublicLibraryCatalog.WebOrigin + "\n"
              + envOriginLine + "\n"
              + "Firebase fallback (website default): " + PublicLibraryCatalog.FirebaseHostingOrigin
            : "Releases & updates: " + AppUpdateService.GitHubRepoUrl + "/releases\n"
              + "Web portal origin: " + PublicLibraryCatalog.WebOrigin + "\n"
              + envOriginLine + "\n"
              + "Firebase fallback (website default): " + PublicLibraryCatalog.FirebaseHostingOrigin;

        // ProcessPath works for single-file publish; Assembly.Location is empty there (IL3000).
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            exePath = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(exePath))
            RuntimeBlock.Text = "Runtime path: (unknown)";
        else if (File.Exists(exePath))
        {
            var utc = File.GetLastWriteTimeUtc(exePath);
            RuntimeBlock.Text =
                "This window shows which copy of the app is running.\r\n" +
                $"Exe: {exePath}\r\n" +
                $"Last write (UTC): {utc:yyyy-MM-dd HH:mm:ss}\r\n" +
                "If you do not see new UI (e.g. Maps → Map label), compare this path to your build output folder.";
        }
        else if (Directory.Exists(exePath))
        {
            var utc = Directory.GetLastWriteTimeUtc(exePath);
            RuntimeBlock.Text =
                "This window shows which copy of the app is running.\r\n" +
                $"App folder: {exePath}\r\n" +
                $"Last write (UTC): {utc:yyyy-MM-dd HH:mm:ss}\r\n" +
                "If you do not see new UI (e.g. Maps → Map label), compare this path to your build output folder.";
        }
        else
            RuntimeBlock.Text = $"Runtime path not found on disk: {exePath}";

        var entitlement = AccountSessionState.LastEntitlement;
        SubscriptionBlock.Text = AccountStatusFormatter.FormatAboutSubscription(entitlement);
        ManageSubscriptionButton.Visibility = entitlement?.IsEntitled == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ManageSubscription_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AccountLinks.PlayStoreAppUrl) { UseShellExecute = true });

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
