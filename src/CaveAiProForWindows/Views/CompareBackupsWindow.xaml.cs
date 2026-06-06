using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class CompareBackupsWindow : Window
{
    private string? _pathA;
    private string? _pathB;
    private Dictionary<string, CaveProjectDocument> _mapA = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CaveProjectDocument> _mapB = new(StringComparer.OrdinalIgnoreCase);

    public CompareBackupsWindow()
    {
        InitializeComponent();
        DiffGrid.ItemsSource = new ObservableCollection<ProjectDiffRow>();
    }

    private void BtnPickA_Click(object sender, RoutedEventArgs e) => Pick(ref _pathA, TxtA);

    private void BtnPickB_Click(object sender, RoutedEventArgs e) => Pick(ref _pathB, TxtB);

    private static void Pick(ref string? field, System.Windows.Controls.TextBlock label)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Compare CaveAI backups",
            Filter = CaveAiBackupFileDialogFilters.CompareBackupsFilter,
        };
        if (dlg.ShowDialog() != true)
            return;
        field = dlg.FileName;
        label.Text = Path.GetFileName(field);
        label.ToolTip = field;
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pathA) || string.IsNullOrEmpty(_pathB))
        {
            MessageBox.Show("Choose both files.", "Compare backups", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var listA = ExplorationDataLoader.LoadAuto(_pathA!).ToList();
            var listB = ExplorationDataLoader.LoadAuto(_pathB!).ToList();
            _mapA = listA.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            _mapB = listB.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var names = _mapA.Keys.Union(_mapB.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var rows = (ObservableCollection<ProjectDiffRow>)DiffGrid.ItemsSource;
            rows.Clear();
            foreach (var name in names)
            {
                _mapA.TryGetValue(name, out var a);
                _mapB.TryGetValue(name, out var b);
                var sa = a?.Shots.Count ?? -1;
                var sb = b?.Shots.Count ?? -1;
                var ra = a?.RocksCount ?? -1;
                var rb = b?.RocksCount ?? -1;
                var ca = a?.FieldCatalogEntryCount ?? -1;
                var cb = b?.FieldCatalogEntryCount ?? -1;
                rows.Add(new ProjectDiffRow(
                    name,
                    sa < 0 ? "—" : sa.ToString(),
                    sb < 0 ? "—" : sb.ToString(),
                    sa < 0 || sb < 0 ? "—" : (sb - sa).ToString(),
                    ra < 0 ? "—" : ra.ToString(),
                    rb < 0 ? "—" : rb.ToString(),
                    ca < 0 ? "—" : ca.ToString(),
                    cb < 0 ? "—" : cb.ToString()));
            }

            if (rows.Count > 0)
                DiffGrid.SelectedIndex = 0;
            else
                ClearPreviews();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Compare backups", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DiffGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiffGrid.SelectedItem is not ProjectDiffRow row)
        {
            ClearPreviews();
            return;
        }

        PreviewTitle.Text = $"Plan preview — {row.ProjectName}";
        _mapA.TryGetValue(row.ProjectName, out var a);
        _mapB.TryGetValue(row.ProjectName, out var b);
        PreviewA.Source = CompareBackupPlanPreview.TryRenderMiniPlan(a);
        PreviewB.Source = CompareBackupPlanPreview.TryRenderMiniPlan(b);
    }

    private void ClearPreviews()
    {
        PreviewTitle.Text = "Plan preview (select a row)";
        PreviewA.Source = null;
        PreviewB.Source = null;
    }

    public sealed record ProjectDiffRow(
        string ProjectName,
        string ShotsA,
        string ShotsB,
        string ShotsDelta,
        string RocksA,
        string RocksB,
        string CatalogA,
        string CatalogB);
}
