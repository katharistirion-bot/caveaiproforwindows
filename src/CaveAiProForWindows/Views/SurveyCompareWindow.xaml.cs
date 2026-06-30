using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class SurveyCompareWindow : Window
{
    private static readonly SolidColorBrush ChangedRowBrush = new(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
    private static readonly SolidColorBrush AddedRowBrush = new(Color.FromArgb(0x33, 0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush RemovedRowBrush = new(Color.FromArgb(0x33, 0xEF, 0x44, 0x44));

    private string? _pathA;
    private string? _pathB;
    private Dictionary<string, CaveProjectDocument> _mapA = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CaveProjectDocument> _mapB = new(StringComparer.OrdinalIgnoreCase);

    public SurveyCompareWindow()
    {
        InitializeComponent();
        LegGrid.ItemsSource = new ObservableCollection<SurveyCompareService.LegDiffRow>();
        StationGrid.ItemsSource = new ObservableCollection<SurveyCompareService.StationDiffRow>();
        LegGrid.Columns.Add(new DataGridTextColumn { Header = "Change", Binding = new System.Windows.Data.Binding("ChangeKind"), Width = 80 });
        LegGrid.Columns.Add(new DataGridTextColumn { Header = "From", Binding = new System.Windows.Data.Binding("FromStation"), Width = 90 });
        LegGrid.Columns.Add(new DataGridTextColumn { Header = "To", Binding = new System.Windows.Data.Binding("ToStation"), Width = 90 });
        LegGrid.Columns.Add(new DataGridTextColumn { Header = "File A", Binding = new System.Windows.Data.Binding("DetailA"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        LegGrid.Columns.Add(new DataGridTextColumn { Header = "File B", Binding = new System.Windows.Data.Binding("DetailB"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        StationGrid.Columns.Add(new DataGridTextColumn { Header = "Change", Binding = new System.Windows.Data.Binding("ChangeKind"), Width = 80 });
        StationGrid.Columns.Add(new DataGridTextColumn { Header = "Station", Binding = new System.Windows.Data.Binding("Station"), Width = 100 });
        StationGrid.Columns.Add(new DataGridTextColumn { Header = "File A", Binding = new System.Windows.Data.Binding("DetailA"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        StationGrid.Columns.Add(new DataGridTextColumn { Header = "File B", Binding = new System.Windows.Data.Binding("DetailB"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        LegGrid.LoadingRow += DiffGrid_LoadingRow;
        StationGrid.LoadingRow += DiffGrid_LoadingRow;
    }

    public static void ShowWithPaths(Window? owner, string pathA, string pathB, string? preferredProjectName = null)
    {
        var w = new SurveyCompareWindow { Owner = owner };
        w._pathA = pathA;
        w._pathB = pathB;
        w.TxtA.Text = Path.GetFileName(pathA);
        w.TxtA.ToolTip = pathA;
        w.TxtB.Text = Path.GetFileName(pathB);
        w.TxtB.ToolTip = pathB;
        w.Compare_Click(w, new RoutedEventArgs());
        if (!string.IsNullOrWhiteSpace(preferredProjectName) && w.ProjectCombo.Items.Count > 0)
        {
            foreach (var item in w.ProjectCombo.Items)
            {
                if (item is string name &&
                    string.Equals(name, preferredProjectName, StringComparison.OrdinalIgnoreCase))
                {
                    w.ProjectCombo.SelectedItem = item;
                    w.RunCompare();
                    break;
                }
            }
        }

        w.ShowDialog();
    }

    private static void DiffGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.Item is not { } item)
            return;
        var kind = item.GetType().GetProperty("ChangeKind")?.GetValue(item) as string ?? "";
        e.Row.Background = kind switch
        {
            "Changed" => ChangedRowBrush,
            "Added" => AddedRowBrush,
            "Removed" => RemovedRowBrush,
            _ => Brushes.Transparent,
        };
    }

    private void PickA_Click(object sender, RoutedEventArgs e) => Pick(ref _pathA, TxtA);

    private void PickB_Click(object sender, RoutedEventArgs e) => Pick(ref _pathB, TxtB);

    private static void Pick(ref string? field, TextBlock label)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Survey compare — pick backup",
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
            MessageBox.Show("Choose both files.", "Survey compare", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _mapA = IndexProjects(ExplorationDataLoader.LoadAuto(_pathA!));
            _mapB = IndexProjects(ExplorationDataLoader.LoadAuto(_pathB!));
            var names = _mapA.Keys.Union(_mapB.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            ProjectCombo.ItemsSource = names;
            if (names.Count > 0)
                ProjectCombo.SelectedIndex = 0;
            RunCompare();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Survey compare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RunCompare()
    {
        if (ProjectCombo.SelectedItem is not string name)
            return;
        _mapA.TryGetValue(name, out var a);
        _mapB.TryGetValue(name, out var b);
        var result = SurveyCompareService.Compare(a, b);
        SummaryText.Text =
            $"Legs: +{result.LegsAdded} / −{result.LegsRemoved} / Δ{result.LegsChanged} · " +
            $"Stations: +{result.StationsAdded} / −{result.StationsRemoved} / moved {result.StationsMoved}";

        var legs = (ObservableCollection<SurveyCompareService.LegDiffRow>)LegGrid.ItemsSource;
        legs.Clear();
        foreach (var row in result.LegChanges)
            legs.Add(row);

        var stations = (ObservableCollection<SurveyCompareService.StationDiffRow>)StationGrid.ItemsSource;
        stations.Clear();
        foreach (var row in result.StationChanges)
            stations.Add(row);
    }

    private static Dictionary<string, CaveProjectDocument> IndexProjects(IEnumerable<CaveProjectDocument> list) =>
        list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
}
