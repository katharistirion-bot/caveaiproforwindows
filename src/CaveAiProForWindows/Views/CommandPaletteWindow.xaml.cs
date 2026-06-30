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
    void Add(string label, Action action) => _all.Add(new CommandPaletteItem(label, action));
    Add("Open backup…", () => _vm.OpenFileCommand.Execute(null));
    Add("Save project", () => _vm.SaveProjectCommand.Execute(null));
    Add("Public Library…", () => _vm.OpenPublicLibraryWithPickerCommand.Execute(null));
    Add("Survey compare…", () => _vm.CompareSurveysCommand.Execute(null));
    Add("Push to Cloud", () => _vm.PublishToCloudCommand.Execute(null));
    Add("Download from Public Library…", () => _vm.DownloadPublicLibraryBackupCommand.Execute(null));
    Add("Keyboard shortcuts", () => _vm.ShowKeyboardShortcutsCommand.Execute(null));
    Add("About", () => _vm.AboutCommand.Execute(null));
    ApplyFilter();
  }

  private void ApplyFilter()
  {
    var q = (FilterBox.Text ?? "").Trim();
    _filtered.Clear();
    foreach (var item in _all)
    {
      if (string.IsNullOrEmpty(q) || item.Label.Contains(q, StringComparison.OrdinalIgnoreCase))
        _filtered.Add(item);
    }
    if (_filtered.Count > 0)
      CommandList.SelectedIndex = 0;
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

  private sealed record CommandPaletteItem(string Label, Action Action);
}
