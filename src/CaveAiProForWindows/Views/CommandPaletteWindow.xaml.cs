using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

public partial class CommandPaletteWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<CommandPaletteItem> _all = new();
    private readonly ObservableCollection<CommandPaletteItem> _filtered = new();

    public CommandPaletteWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        CommandList.ItemsSource = _filtered;
        BuildItems();
        FilterBox.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) => { FilterBox.Focus(); FilterBox.SelectAll(); };
    }

    private void BuildItems()
    {
        void Add(string category, string label, Action action, params string[] keywords) =>
            _all.Add(new CommandPaletteItem(category, label, action, keywords));

        Add("File", "Open project…", () => _vm.OpenFileCommand.Execute(null), "open", "project", "backup", "json", "zip");
        Add("File", "Open backup…", () => _vm.OpenFileCommand.Execute(null), "open", "load", "zip", "json");
        Add("File", "Import Survex…", () => _vm.ImportSurvexCommand.Execute(null), "svx", "survex", "import");
        Add("File", "Save project", () => _vm.SaveProjectCommand.Execute(null), "save", "write");
        Add("File", "Close workspace", () => _vm.CloseWorkspaceCommand.Execute(null), "close", "unload");
        Add("View", "Surface map tab", () => _vm.OpenSurfaceMapTabCommand.Execute(null), "surface", "terrain", "hillshade", "map");
        Add("Library", "Public Library…", () => _vm.OpenPublicLibraryWithPickerCommand.Execute(null), "catalog", "reference");
        Add("Library", "Reference catalog", () => _vm.OpenPublicLibraryCatalogCommand.Execute(null), "caves", "index");
        Add("Library", "Download from Public Library…", () => _vm.DownloadPublicLibraryBackupCommand.Execute(null), "cloud", "backup");
        Add("Survey", "Survey compare…", () => _vm.CompareSurveysCommand.Execute(null), "diff", "compare");
        Add("Survey", "Compare backups…", () => _vm.CompareBackupsCommand.Execute(null), "diff", "files");
        Add("Survey", "Field trip planner…", () => _vm.OpenFieldTripPlannerCommand.Execute(null), "route", "gpx");
        Add("Cloud", "Push to Cloud", () => _vm.PublishToCloudCommand.Execute(null), "publish", "upload");
        Add("Cloud", "Retry failed cloud publish", () => _vm.CloudCommands.RetryCommand.Execute(null), "retry", "queue");
        Add("Export", "Export plan SVG…", () => _vm.ExportPlanSvgCommand.Execute(null), "svg", "plan");
        Add("Export", "Export Survex…", () => _vm.ExportSurvexCommand.Execute(null), "svx", "survex");
        Add("Export", "Export plan DXF…", () => _vm.ExportPlanDxfCommand.Execute(null), "dxf", "plan", "cad");
        Add("Help", "Keyboard shortcuts", () => _vm.ShowKeyboardShortcutsCommand.Execute(null), "keys", "hotkeys");
        Add("Help", "About", () => _vm.AboutCommand.Execute(null), "version");
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = (FilterBox.Text ?? "").Trim();
        _filtered.Clear();
        var ranked = _all
            .Select(item => (item, score: Score(item, q)))
            .Where(x => x.score >= 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.item.Label, StringComparer.OrdinalIgnoreCase);
        foreach (var (item, _) in ranked)
            _filtered.Add(item);
        if (_filtered.Count > 0)
            CommandList.SelectedIndex = 0;
    }

    private static int Score(CommandPaletteItem item, string query)
    {
        if (string.IsNullOrEmpty(query))
            return 0;
        if (item.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (item.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 80;
        if (item.Keywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return 60;
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 1 && tokens.All(t =>
                item.Label.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                item.Keywords.Any(k => k.Contains(t, StringComparison.OrdinalIgnoreCase))))
            return 40;
        return -1;
    }

    private void RunSelected()
    {
        if (CommandList.SelectedItem is not CommandPaletteItem item)
            return;
        DialogResult = true;
        Close();
        item.Action();
    }

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { CommandList.Focus(); CommandList.SelectedIndex = 0; e.Handled = true; }
        else if (e.Key == Key.Enter) { RunSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
    }

    private void CommandList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { RunSelected(); e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RunSelected();

    private sealed record CommandPaletteItem(string Category, string Label, Action Action, string[] Keywords);
}
