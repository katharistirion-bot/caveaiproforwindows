using System.Collections.ObjectModel;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class NetworkAnchorsWindow : Window
{
    private readonly CaveProjectDocument _project;
    private readonly ObservableCollection<EquateRow> _equates = new();
    private readonly ObservableCollection<FixedRow> _fixed = new();

    public bool Applied { get; private set; }

    public NetworkAnchorsWindow(CaveProjectDocument project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        InitializeComponent();
        LoadFromProject();
        EquateGrid.ItemsSource = _equates;
        FixedGrid.ItemsSource = _fixed;
        Title = $"Network anchors — {project.Name}";
    }

    private void LoadFromProject()
    {
        _equates.Clear();
        if (_project.StationEquates != null)
        {
            foreach (var eq in _project.StationEquates)
            {
                if (string.IsNullOrWhiteSpace(eq.StationA) && string.IsNullOrWhiteSpace(eq.StationB))
                    continue;
                _equates.Add(new EquateRow { StationA = eq.StationA ?? "", StationB = eq.StationB ?? "" });
            }
        }

        _fixed.Clear();
        var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_project.FixedStations != null)
        {
            foreach (var name in _project.FixedStations)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    held.Add(name.Trim());
            }
        }

        var positions = _project.FixedStationPositions;
        if (positions != null)
        {
            foreach (var kv in positions)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;
                held.Add(kv.Key.Trim());
                _fixed.Add(new FixedRow
                {
                    Name = kv.Key.Trim(),
                    Hold = true,
                    XText = FormatCoord(kv.Value.X),
                    YText = FormatCoord(kv.Value.Y),
                    ZText = FormatCoord(kv.Value.Z),
                });
            }
        }

        foreach (var name in held.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (_fixed.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            _fixed.Add(new FixedRow { Name = name, Hold = true, XText = "", YText = "", ZText = "" });
        }
    }

    private static string FormatCoord(float v) =>
        v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private void AddEquate_Click(object sender, RoutedEventArgs e) =>
        _equates.Add(new EquateRow());

    private void RemoveEquate_Click(object sender, RoutedEventArgs e)
    {
        if (EquateGrid.SelectedItem is EquateRow row)
            _equates.Remove(row);
    }

    private void AddFixed_Click(object sender, RoutedEventArgs e) =>
        _fixed.Add(new FixedRow { Hold = true });

    private void RemoveFixed_Click(object sender, RoutedEventArgs e)
    {
        if (FixedGrid.SelectedItem is FixedRow row)
            _fixed.Remove(row);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        EquateGrid.CommitEdit();
        EquateGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        FixedGrid.CommitEdit();
        FixedGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var result = NetworkAnchorsApplier.TryApply(
            _project,
            _equates.Select(r => new NetworkAnchorsApplier.EquateDraft(r.StationA, r.StationB)),
            _fixed.Select(r => new NetworkAnchorsApplier.FixedDraft(r.Name, r.Hold, r.XText, r.YText, r.ZText)));

        if (!result.Ok)
        {
            MessageBox.Show(
                this,
                result.ErrorMessage ?? "Could not apply network anchors.",
                "Network anchors",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Applied = true;
        DialogResult = true;
        Close();
    }

    private sealed class EquateRow
    {
        public string StationA { get; set; } = "";
        public string StationB { get; set; } = "";
    }

    private sealed class FixedRow
    {
        public string Name { get; set; } = "";
        public bool Hold { get; set; }
        public string XText { get; set; } = "";
        public string YText { get; set; } = "";
        public string ZText { get; set; } = "";
    }
}
