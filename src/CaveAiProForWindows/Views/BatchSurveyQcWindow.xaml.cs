using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class BatchSurveyQcWindow : Window
{
    private IReadOnlyList<BatchSurveyQcResult> _results = [];

    public BatchSurveyQcWindow()
    {
        InitializeComponent();
    }

    public static void ShowDialog(Window? owner)
    {
        new BatchSurveyQcWindow { Owner = owner }.ShowDialog();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Folder with survey backups" };
        if (dlg.ShowDialog(this) == true)
            FolderBox.Text = dlg.FolderName;
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show(this, "Choose a valid folder.", "Batch QC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _results = BatchSurveyQcService.ScanFolder(folder);
        ResultsGrid.ItemsSource = _results;
        StatusText.Text = $"Scanned {_results.Count} project(s).";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0)
        {
            MessageBox.Show(this, "Scan a folder first.", "Batch QC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new OpenFolderDialog { Title = "Save batch QC report" };
        if (dlg.ShowDialog(this) != true)
            return;

        var written = BatchSurveyQcService.ExportReport(_results, dlg.FolderName);
        MessageBox.Show(this, $"Wrote {written} file(s) to {dlg.FolderName}", "Batch QC", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
