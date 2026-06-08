using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class RecentPathRenameWindow : Window
{
    private string _targetPath = "";

    public RecentPathRenameWindow()
    {
        InitializeComponent();
    }

    public string? NewFullPath { get; private set; }

    public static bool TryPrompt(Window? owner, string path, out string? newFullPath)
    {
        newFullPath = null;
        if (string.IsNullOrWhiteSpace(path) || !RecentPathFileOps.Exists(path))
            return false;

        var dlg = new RecentPathRenameWindow
        {
            Owner = owner,
            _targetPath = path,
        };
        dlg.TitleText.Text = RecentPathFileOps.IsDirectory(path) ? "Rename folder" : "Rename file";
        dlg.LocationBox.Text = path;
        dlg.NameBox.Text = RecentPathFileOps.IsDirectory(path)
            ? new DirectoryInfo(path).Name
            : Path.GetFileName(path);
        dlg.NameBox.SelectAll();
        dlg.NameBox.Focus();

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.NewFullPath))
            return false;

        newFullPath = dlg.NewFullPath;
        return true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var trimmed = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            MessageBox.Show(this, "Enter a name.", "Rename", MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        if (!RecentPathFileOps.TryRename(_targetPath, trimmed, out var newFull, out var error))
        {
            MessageBox.Show(this, error ?? "Rename failed.", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
            NameBox.Focus();
            NameBox.SelectAll();
            return;
        }

        NewFullPath = newFull;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
