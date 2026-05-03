using System.IO;
using System.Reflection;
using System.Windows;

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

        var exePath = !string.IsNullOrWhiteSpace(asm.Location)
            ? asm.Location
            : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            RuntimeBlock.Text = "Runtime path: (unknown — single-file publish?)";
        else if (File.Exists(exePath))
        {
            var utc = File.GetLastWriteTimeUtc(exePath);
            RuntimeBlock.Text =
                "This window shows which copy of the app is running.\r\n" +
                $"Exe: {exePath}\r\n" +
                $"Last write (UTC): {utc:yyyy-MM-dd HH:mm:ss}\r\n" +
                "If you do not see new UI (e.g. Maps → Map label), compare this path to your build output folder.";
        }
        else
            RuntimeBlock.Text = $"Exe path not found on disk: {exePath}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
