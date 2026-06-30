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
                var sta = StationCount(a);
                var stb = StationCount(b);
                var symA = SymbolCount(a);
                var symB = SymbolCount(b);
                var ra = a?.RocksCount ?? -1;
                var rb = b?.RocksCount ?? -1;
                var ca = a?.FieldCatalogEntryCount ?? -1;
                var cb = b?.FieldCatalogEntryCount ?? -1;
                rows.Add(new ProjectDiffRow(
                    name,
                    FormatCount(sta),
                    FormatCount(stb),
                    FormatDelta(sta, stb),
                    FormatCount(symA),
                    FormatCount(symB),
                    FormatDelta(symA, symB),
                    sa < 0 ? "—" : sa.ToString(),
                    sb < 0 ? "—" : sb.ToString(),
                    sa < 0 || sb < 0 ? "—" : (sb - sa).ToString(),
                    ra < 0 ? "—" : ra.ToString(),
                    rb < 0 ? "—" : rb.ToString(),
                    ca < 0 ? "—" : ca.ToString(),
                    cb < 0 ? "—" : cb.ToString(),
                    BuildSummary(a, b, sta, stb, symA, symB, sa, sb, ca, cb)));
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

    private static int StationCount(CaveProjectDocument? project) =>
        project == null ? -1 : SurveyStationGeometry.CalculatePlanCoordinates(project).Count;

    private static int SymbolCount(CaveProjectDocument? project) =>
        project == null ? -1 : SurveyStationGeometry.ParsePlanMapSymbols(project).Count;

    private static string FormatCount(int n) => n < 0 ? "—" : n.ToString();

    private static string FormatDelta(int a, int b)
    {
        if (a < 0 || b < 0)
            return "—";
        var d = b - a;
        return d == 0 ? "0" : d > 0 ? $"+{d}" : d.ToString();
    }

    private static string BuildSummary(
        CaveProjectDocument? a,
        CaveProjectDocument? b,
        int sta,
        int stb,
        int symA,
        int symB,
        int sa,
        int sb,
        int ca,
        int cb)
    {
        if (a == null)
            return "Only in file B";
        if (b == null)
            return "Only in file A";

        var parts = new List<string>();
        if (sta != stb)
            parts.Add($"stations {FormatDelta(sta, stb)}");
        if (symA != symB)
            parts.Add($"symbols {FormatDelta(symA, symB)}");
        if (ca != cb)
            parts.Add($"catalog {FormatDelta(ca, cb)}");
        if (sa != sb)
            parts.Add($"shots {FormatDelta(sa, sb)}");
        return parts.Count == 0 ? "No differences" : string.Join(" · ", parts);
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
        RefreshPlanPreviews(a, b);
        if (DiffSummaryText != null)
            DiffSummaryText.Text = row.Summary;
    }

    private void OverlayDiffCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (DiffGrid.SelectedItem is ProjectDiffRow row)
        {
            _mapA.TryGetValue(row.ProjectName, out var a);
            _mapB.TryGetValue(row.ProjectName, out var b);
            RefreshPlanPreviews(a, b);
        }
    }

    private void RefreshPlanPreviews(CaveProjectDocument? a, CaveProjectDocument? b)
    {
        var overlay = OverlayDiffCheck?.IsChecked == true;
        if (overlay)
        {
            SideBySidePanel.Visibility = Visibility.Collapsed;
            OverlayPanel.Visibility = Visibility.Visible;
            PreviewOverlay.Source = CompareBackupPlanPreview.TryRenderOverlayPlan(a, b);
            PreviewA.Source = null;
            PreviewB.Source = null;
        }
        else
        {
            SideBySidePanel.Visibility = Visibility.Visible;
            OverlayPanel.Visibility = Visibility.Collapsed;
            PreviewA.Source = CompareBackupPlanPreview.TryRenderMiniPlan(a);
            PreviewB.Source = CompareBackupPlanPreview.TryRenderMiniPlan(b);
            PreviewOverlay.Source = null;
        }
    }

    private void ClearPreviews()
    {
        PreviewTitle.Text = "Plan preview (select a row)";
        PreviewA.Source = null;
        PreviewB.Source = null;
        PreviewOverlay.Source = null;
        if (DiffSummaryText != null)
            DiffSummaryText.Text = "";
    }

    public sealed record ProjectDiffRow(
        string ProjectName,
        string StationsA,
        string StationsB,
        string StationsDelta,
        string SymbolsA,
        string SymbolsB,
        string SymbolsDelta,
        string ShotsA,
        string ShotsB,
        string ShotsDelta,
        string RocksA,
        string RocksB,
        string CatalogA,
        string CatalogB,
        string Summary);
}
