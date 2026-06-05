using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Secrets;
using CaveAiProForWindows.ViewModels;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        InputBindings.Add(new KeyBinding(vm.OpenFileCommand, Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.CloseWorkspaceCommand, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.AboutCommand, new KeyGesture(Key.F1)));

        AllowDrop = true;
        AddHandler(System.Windows.DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(OnPreviewDragOver), handledEventsToo: true);
        AddHandler(System.Windows.DragDrop.DropEvent, new System.Windows.DragEventHandler(OnDrop), handledEventsToo: true);

        Loaded += (_, _) =>
        {
            WindowPlacementStore.ApplyTo(this);
            RefreshReplicateTokenStatusUi();
            SurveyWorkspaceNavigator.Register(this);
        };
        Closing += (_, _) => WindowPlacementStore.SaveFrom(this);
        PreviewKeyDown += OnMainWindowPreviewKeyDown;
        App.WriteStartupLog("MainWindow constructed and Loaded wiring attached");
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject? ancestor)
    {
        while (child != null)
        {
            if (ReferenceEquals(child, ancestor))
                return true;
            child = VisualTreeHelper.GetParent(child);
        }

        return false;
    }

    private bool IsKeyboardFocusWithinSurveyTabs() =>
        MainSurveyTabControl != null &&
        Keyboard.FocusedElement is DependencyObject dep &&
        IsDescendantOf(dep, MainSurveyTabControl);

    private static bool IsTextInputFocused() =>
        Keyboard.FocusedElement is TextBoxBase;

    private IMapSurfaceShortcuts? TryResolveFocusedMapSurface()
    {
        if (Keyboard.FocusedElement is not DependencyObject dep)
            return null;
        for (var o = dep; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (o is PlanView pv && pv.VisualizationMode != SurveyVisualizationMode.Pseudo3D)
                return pv;
            if (o is SketchEditorView sk)
                return sk;
            if (o is SectionView sec)
                return sec;
        }

        return null;
    }

    private void OnMainWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsKeyboardFocusWithinSurveyTabs())
            return;

        if (e.Key == Key.Escape)
        {
            if (IsTextInputFocused())
                return;
            var surfaceEsc = TryResolveFocusedMapSurface();
            if (surfaceEsc == null)
                return;
            surfaceEsc.ClearMapSelectionAndRedraw();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        if (IsTextInputFocused())
            return;

        var surface = TryResolveFocusedMapSurface();
        if (surface == null)
            return;

        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.PanZoom);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.Select);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.DrawFreehand);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                surface.ApplyMapEditorTool(MapCanvasEditorTool.PlaceSymbol);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                surface.ResetMapView();
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                surface.MapZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                surface.MapZoomOut();
                e.Handled = true;
                break;
        }
    }

    private void IntegrityBanner_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IntegrityTabItem != null)
            IntegrityTabItem.IsSelected = true;
    }

    private void OpenPublicLibraryExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicLibraryCatalog.OpenMap();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Public Cave Library", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenWebCaveAiExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicLibraryCatalog.OpenCaveAi();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Cave AI", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OpenDataInspector_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.LegalTermsAccepted)
        {
            MessageBox.Show(
                this,
                "Please open the LEGAL & SETTINGS tab and accept the terms and conditions before using the Data & backup inspector.",
                "Terms required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectLegalSettingsTab();
            return;
        }

        var w = new DataInspectorWindow
        {
            Owner = this,
            DataContext = DataContext,
        };
        w.Show();
    }

    public void SelectLegalSettingsTab()
    {
        if (LegalSettingsTabItem != null)
            LegalSettingsTabItem.IsSelected = true;
    }

    private void LegalRequiredOverlay_GoToLegal_Click(object sender, RoutedEventArgs e) => SelectLegalSettingsTab();

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
                    ? "Added 1 standalone map — use File → Export → Maps report (CSV) to list paths."
                    : $"Added {n} standalone maps — use File → Export → Maps report (CSV) to list paths.";
        }

        e.Handled = true;
    }

    private void ExportProjectWithPlanZip_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;
        if (!vm.LegalTermsAccepted)
        {
            MessageBox.Show(
                this,
                "Please open the LEGAL & SETTINGS tab and accept the terms and conditions before exporting.",
                "Terms required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SelectLegalSettingsTab();
            return;
        }

        var project = vm.SelectedProject;
        if (project == null)
        {
            MessageBox.Show(
                "Select a project in the list first.",
                "Export ZIP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var seg = string.Join("_", (project.Name ?? "cave").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim('_');
        if (seg.Length == 0)
            seg = "cave";
        if (seg.Length > 60)
            seg = seg[..60].TrimEnd('_');

        var dlg = new SaveFileDialog
        {
            Title = "Export project to ZIP",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"{seg}_CaveAiPro_export.zip",
            AddExtension = true,
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var png = PlanViewControl.CapturePlanPngBytes() ?? Array.Empty<byte>();
            SurveyPortableZipExporter.WriteZip(project, png, dlg.FileName);
            vm.StatusMessage = $"Exported ZIP: {dlg.FileName}";
            MessageBox.Show(
                "Saved:\r\n• data.json — one project (Android-compatible Gson shape)\r\n• plan_view.png — PLAN tab snapshot\r\n• README.txt",
                "Export ZIP",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export ZIP failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshReplicateTokenStatusUi()
    {
        if (ReplicateTokenStatusText == null)
            return;
        ReplicateTokenStatusText.Text = ReplicateApiTokenStore.IsConfigured()
            ? "Status: token configured (Windows Credential Manager or environment variable)."
            : "Status: no token saved yet.";
    }

    private void SaveReplicateApiToken_Click(object sender, RoutedEventArgs e)
    {
        var token = ReplicateApiTokenBox?.Password?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this, "Paste your Replicate API token first.", "Replicate API token",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ReplicateApiTokenStore.TrySave(token))
        {
            MessageBox.Show(this, "Could not save the token to Windows Credential Manager.", "Replicate API token",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ReplicateApiTokenBox!.Password = "";
        RefreshReplicateTokenStatusUi();
        MessageBox.Show(this, "Replicate API token saved to Windows Credential Manager.", "Replicate API token",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearReplicateApiToken_Click(object sender, RoutedEventArgs e)
    {
        ReplicateApiTokenStore.TryClear();
        if (ReplicateApiTokenBox != null)
            ReplicateApiTokenBox.Password = "";
        RefreshReplicateTokenStatusUi();
        MessageBox.Show(this, "Cleared the saved Replicate token from Windows Credential Manager.", "Replicate API token",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Focus PLAN tab and zoom X-Ray / Plan to the given station (called from navigator hub).</summary>
    public void FocusStationOnWorkspace(string stationName)
    {
        if (string.IsNullOrWhiteSpace(stationName))
            return;

        if (MainSurveyTabControl != null)
            MainSurveyTabControl.SelectedIndex = 0;

        PlanViewControl?.ApplyExternalStationSelection(stationName);
        PlanViewControl?.ZoomToStation(stationName);
        OfflineXRayViewControl?.ZoomToStation(stationName);
    }
}
