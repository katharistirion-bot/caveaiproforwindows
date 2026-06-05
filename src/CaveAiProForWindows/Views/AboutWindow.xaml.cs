using System.IO;
using System.Reflection;
using System.Windows;
using CaveAiProForWindows.Services;

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

        var envOrigin = Environment.GetEnvironmentVariable("CAVEAIPRO_WEB_ORIGIN");
        WebPortalBlock.Text =
            "Web portal origin: " + PublicLibraryCatalog.WebOrigin + "\n" +
            (string.IsNullOrWhiteSpace(envOrigin)
                ? "Set CAVEAIPRO_WEB_ORIGIN if the custom domain differs from the default."
                : "CAVEAIPRO_WEB_ORIGIN is set for this user/machine.") +
            "\nFirebase fallback (website default): " + PublicLibraryCatalog.FirebaseHostingOrigin;

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
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
