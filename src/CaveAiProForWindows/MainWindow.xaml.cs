using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        InputBindings.Add(new KeyBinding(vm.OpenFileCommand, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.AboutCommand, new KeyGesture(Key.F1)));

        AllowDrop = true;
        AddHandler(System.Windows.DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(OnPreviewDragOver), handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DropEvent, new System.Windows.DragEventHandler(OnDrop), handledEventsToo: true);

        Loaded += (_, _) => WindowPlacementStore.ApplyTo(this);
        Closing += (_, _) => WindowPlacementStore.SaveFrom(this);
        App.WriteStartupLog("MainWindow constructed and Loaded wiring attached");
    }

    private static bool IsSurveyBackupFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".json" or ".zip";
    }

    private void OnPreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files &&
            files.Any(static f =>
                !string.IsNullOrEmpty(f) &&
                (IsSurveyBackupFile(f) || StandaloneMapFileSupport.IsStandaloneMapFile(f))))
            e.Effects = System.Windows.DragDropEffects.Copy;
        else
            e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || DataContext is not MainViewModel vm)
        {
            e.Handled = true;
            return;
        }

        var existing = files
            .Where(static f => !string.IsNullOrEmpty(f) && File.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var surveys = existing.Where(IsSurveyBackupFile).ToList();
        var maps = existing.Where(StandaloneMapFileSupport.IsStandaloneMapFile).ToList();

        if (surveys.Count > 0)
            vm.LoadFromPaths(surveys);

        if (maps.Count > 0)
        {
            var n = vm.AddStandaloneMapPaths(maps);
            if (n > 0 && surveys.Count == 0)
                vm.StatusMessage = n == 1
                    ? "Added 1 standalone map — open the Maps tab."
                    : $"Added {n} standalone maps — open the Maps tab.";
        }

        e.Handled = true;
    }
}
