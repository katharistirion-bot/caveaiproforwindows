using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Diagnostics;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

public partial class SectionView : System.Windows.Controls.UserControl, IMapSurfaceShortcuts
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(SectionView),
        new PropertyMetadata(null, (d, _) => ((SectionView)d).Redraw()));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(SectionView),
        new PropertyMetadata(null, (d, _) => ((SectionView)d).Redraw()));

    public static readonly DependencyProperty MapRowsProperty = DependencyProperty.Register(
        nameof(MapRows),
        typeof(IEnumerable),
        typeof(SectionView),
        new PropertyMetadata(null, OnMapRowsChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(SectionView),
        new PropertyMetadata(null, OnMapInventoryChanged));

    public static readonly DependencyProperty VisualizationModeProperty = DependencyProperty.Register(
        nameof(VisualizationMode),
        typeof(SurveyVisualizationMode),
        typeof(SectionView),
        new PropertyMetadata(SurveyVisualizationMode.Standard, OnVisualizationModeChanged));

    private static void OnVisualizationModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        if (!v.IsLoaded)
            return;
        v.ZoomPan.X = 0;
        v.ZoomPan.Y = 0;
        v.ZoomScale.ScaleX = 1;
        v.ZoomScale.ScaleY = 1;
        v.FitMapSurfaceToHost();
        v.Redraw();
        v.PersistSectionTab();
    }

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.Redraw();
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (SectionView)d;
        v.UnwireMapInventory(e.OldValue);
        v.WireMapInventory(e.NewValue);
        v.Redraw();
    }

    private INotifyCollectionChanged? _wiredMapRows;
    private INotifyCollectionChanged? _wiredMapInventory;

    private void WireMapRows(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            _wiredMapRows = n;
            n.CollectionChanged += MapRows_CollectionChanged;
        }
    }

    private void UnwireMapRows(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            n.CollectionChanged -= MapRows_CollectionChanged;
            if (ReferenceEquals(n, _wiredMapRows))
                _wiredMapRows = null;
        }
    }

    private void MapRows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    private void WireMapInventory(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            _wiredMapInventory = n;
            n.CollectionChanged += MapInventory_CollectionChanged;
        }
    }

    private void UnwireMapInventory(object? value)
    {
        if (value is INotifyCollectionChanged n)
        {
            n.CollectionChanged -= MapInventory_CollectionChanged;
            if (ReferenceEquals(n, _wiredMapInventory))
                _wiredMapInventory = null;
        }
    }

    private void MapInventory_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Redraw();

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    public IEnumerable? MapRows
    {
        get => (IEnumerable?)GetValue(MapRowsProperty);
        set => SetValue(MapRowsProperty, value);
    }

    public IEnumerable? MapInventory
    {
        get => (IEnumerable?)GetValue(MapInventoryProperty);
        set => SetValue(MapInventoryProperty, value);
    }

    /// <summary>Native vector style for this tab (set from MainWindow tab headers).</summary>
    public SurveyVisualizationMode VisualizationMode
    {
        get => (SurveyVisualizationMode)GetValue(VisualizationModeProperty);
        set => SetValue(VisualizationModeProperty, value);
    }

    private MapCanvasEditorController? _mapEditor;
    private MapCanvasEditorTool _currentTool = MapCanvasEditorTool.PanZoom;
    private SketchEditorSymbolKind _stampKind = SketchEditorSymbolKind.RockBlock;
    private MainViewModel? _wiredMainVm;
    private CaveProjectDocument? _designLayerProjectScope;
    private bool _applyingSettings;
    private SurveyMapPickHighlight? _surveyPickHighlight;

    public SectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += SectionView_DataContextChanged;
    }

    private void SectionView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        WireMainViewModel(e.NewValue as MainViewModel);

    private void WireMainViewModel(MainViewModel? vm)
    {
        if (_wiredMainVm != null)
            _wiredMainVm.SurveyDataChanged -= MainViewModel_SurveyDataChanged;
        _wiredMainVm = vm;
        if (_wiredMainVm != null)
            _wiredMainVm.SurveyDataChanged += MainViewModel_SurveyDataChanged;
    }

    private void MainViewModel_SurveyDataChanged(object? sender, EventArgs e) => Redraw();

    private MapCanvasEditorTool GetCurrentEditorTool() => _currentTool;

    private SketchEditorSymbolKind GetSelectedSketchStamp() => _stampKind;

    private void SyncSymbolPaletteEnabled()
    {
        if (SymbolPaletteRoot != null)
            SymbolPaletteRoot.IsEnabled = _currentTool == MapCanvasEditorTool.PlaceSymbol;
    }

    private void EnsureSymbolPaletteHasSelection()
    {
        if (SymbolPaletteRock is { IsChecked: true })
            return;
        if (SymbolPaletteWater is { IsChecked: true })
            return;
        if (SymbolPaletteSpele is { IsChecked: true })
            return;
        if (SymbolPaletteRock != null)
            SymbolPaletteRock.IsChecked = true;
    }

    private void SymbolPalette_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { IsChecked: true } t)
            return;
        _stampKind = t switch
        {
            _ when ReferenceEquals(t, SymbolPaletteRock) => SketchEditorSymbolKind.RockBlock,
            _ when ReferenceEquals(t, SymbolPaletteWater) => SketchEditorSymbolKind.WaterPool,
            _ => SketchEditorSymbolKind.StalactiteSpeleothem,
        };
        foreach (ToggleButton sibling in new ToggleButton?[]
                 {
                     SymbolPaletteRock, SymbolPaletteWater, SymbolPaletteSpele,
                 }.OfType<ToggleButton>())
        {
            if (!ReferenceEquals(sibling, t))
                sibling.IsChecked = false;
        }

        PersistSectionTab();
    }

    private void ApplySectionTabFromSettings()
    {
        _applyingSettings = true;
        try
        {
            var s = AppUiSettingsStore.LoadOrDefault().Section;
            if (Enum.TryParse(s.Tool, out MapCanvasEditorTool t))
                _currentTool = t;
            else
                _currentTool = MapCanvasEditorTool.PanZoom;
            if (StationNamesCheck != null)
                StationNamesCheck.IsChecked = s.StationNames;
            if (CartographyOverlayCheck != null)
                CartographyOverlayCheck.IsChecked = s.Overlay;
            if (EditorToolPan != null && EditorToolSelect != null && EditorToolDraw != null &&
                EditorToolSymbol != null)
            {
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
                EditorToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
                EditorToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
            }
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplyDeferredSectionZoomFromSettings()
    {
        var s = AppUiSettingsStore.LoadOrDefault().Section;
        if (ZoomScale == null || ZoomPan == null)
            return;
        var zx = Math.Clamp(s.ZoomScale > 0 ? s.ZoomScale : 1, 0.12, 12.0);
        ZoomScale.ScaleX = zx;
        ZoomScale.ScaleY = zx;
        ZoomPan.X = s.PanX;
        ZoomPan.Y = s.PanY;
    }

    private void PersistSectionTab()
    {
        if (_applyingSettings)
            return;
        var all = AppUiSettingsStore.LoadOrDefault();
        all.Section.Tool = _currentTool.ToString();
        all.Section.StationNames = StationNamesCheck?.IsChecked == true;
        all.Section.Overlay = CartographyOverlayCheck?.IsChecked != false;
        if (ZoomScale != null)
            all.Section.ZoomScale = ZoomScale.ScaleX;
        if (ZoomPan != null)
        {
            all.Section.PanX = ZoomPan.X;
            all.Section.PanY = ZoomPan.Y;
        }

        AppUiSettingsStore.Save(all);
    }

    private void EditorToolPan_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.PanZoom;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolSelect_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.Select;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolDraw_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
            _currentTool = MapCanvasEditorTool.DrawFreehand;
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void EditorToolSymbol_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true })
        {
            _currentTool = MapCanvasEditorTool.PlaceSymbol;
            EnsureSymbolPaletteHasSelection();
        }

        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        if (DesignLayer != null && HostScroll != null && ZoomPan != null)
        {
            _mapEditor = new MapCanvasEditorController(
                DesignLayer, HostScroll, ZoomPan, GetCurrentEditorTool, GetSelectedSketchStamp);
        }

        ApplySectionTabFromSettings();
        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        else if (SymbolPaletteRock != null && SymbolPaletteWater != null && SymbolPaletteSpele != null)
            SymbolPaletteRock.IsChecked = true;
        SyncSymbolPaletteEnabled();

        WireMainViewModel(DataContext as MainViewModel);
        SurveyStationSelectionHub.StationSelected += OnExternalStationSelected;
        Redraw();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitMapSurfaceToHost));
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyDeferredSectionZoomFromSettings));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PersistSectionTab();
        SurveyStationSelectionHub.StationSelected -= OnExternalStationSelected;
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        WireMainViewModel(null);
        if (MapHostGrid != null)
            MapHostGrid.SizeChanged -= MapHostGrid_SizeChanged;
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
    }

    private void MapHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged && !e.HeightChanged)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FitMapSurfaceToHost));
    }

    private void FitMapSurfaceToHost()
    {
        if (MapHostGrid == null || SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null ||
            HostScroll == null || ZoomScale == null)
            return;
        var w = Math.Max(320, MapHostGrid.ActualWidth);
        var h = Math.Max(240, MapHostGrid.ActualHeight);
        if (w < 8 || h < 8)
            return;
        if (Math.Abs(SurveyCanvas.Width - w) < 0.5 && Math.Abs(SurveyCanvas.Height - h) < 0.5)
            return;
        MapZoomRoot.Width = w;
        MapZoomRoot.Height = h;
        SurveyCanvas.Width = w;
        SurveyCanvas.Height = h;
        DesignLayer.Width = w;
        DesignLayer.Height = h;
        ZoomScale.CenterX = w * 0.5;
        ZoomScale.CenterY = h * 0.5;
        Redraw();
    }

    private void OnSurveyCanvasThemeChanged() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Redraw));

    private void MaybeResetDesignLayerForProjectChange()
    {
        if (DesignLayer == null)
            return;
        if (ReferenceEquals(Project, _designLayerProjectScope))
            return;
        _designLayerProjectScope = Project;
        DesignLayer.Children.Clear();
        _mapEditor?.OnDesignLayerCleared();
    }

    private void Redraw()
    {
        if (SurveyCanvas == null || DesignLayer == null || MapZoomRoot == null)
            return;

        MaybeResetDesignLayerForProjectChange();
        SurveyCanvas.Children.Clear();
        var p = Project;
        if (p == null)
        {
            AddMessage("Select a project from the list.");
            AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
            return;
        }

        try
        {
            var underlays = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
            var scene = SectionSceneBuilder.TryBuild(p, VisualizationMode);
            if (scene == null)
            {
                if (underlays.Count > 0)
                {
                    ZoomScale.CenterX = SurveyCanvas.Width / 2;
                    ZoomScale.CenterY = SurveyCanvas.Height / 2;
                    SectionCanvasRenderer.DrawRasterUnderlaysOnly(
                        SurveyCanvas,
                        highContrast: false,
                        SurveyCanvas.Width,
                        SurveyCanvas.Height,
                        underlays,
                        "section");
                    AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                    return;
                }

                AddMessage(
                    "No section vectors/sketches (viewMode 1) or traverse data for this project. "
                    + "If cartography paths resolve to PNG/JPEG/WebP/TIFF (ZIP open, or .zip next to exported data.json), a raster underlay can still appear here.");
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
                return;
            }

            ZoomScale.CenterX = SurveyCanvas.Width / 2;
            ZoomScale.CenterY = SurveyCanvas.Height / 2;
            SectionCanvasRenderer.Draw(
                scene,
                SurveyCanvas,
                highContrast: false,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlays,
                p,
                ZipPath,
                CurrentDrawOptions());
            if (PlanCanvasRenderer.TryComputeSurveyLayout(scene, SurveyCanvas.Width, SurveyCanvas.Height, out var symLayout))
                AndroidImportedSymbolPresenter.SyncDesignLayer(DesignLayer, scene, symLayout, highContrast: false);
            else
                AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SectionView] Redraw failed: {ex}");
            SurveyCanvas.Children.Clear();
            AndroidImportedSymbolPresenter.ClearImported(DesignLayer);
            MessageBox.Show(
                $"Section view could not render this project.\n\n{ex.Message}",
                "Section render error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            AddMessage($"Render error: {ex.Message}");
        }
    }

    private void AddMessage(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 14,
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Cave.TextMuted");
        Canvas.SetLeft(tb, 16);
        Canvas.SetTop(tb, 16);
        SurveyCanvas.Children.Add(tb);
    }

    private void HostScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ZoomScale == null || ZoomPan == null || MapZoomRoot == null)
            return;
        e.Handled = true;
        var focus = e.GetPosition(MapZoomRoot);
        var w = Math.Max(0, MapZoomRoot.ActualWidth);
        var h = Math.Max(0, MapZoomRoot.ActualHeight);
        var clamped = new Point(Math.Clamp(focus.X, 0, w), Math.Clamp(focus.Y, 0, h));
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, e.Delta > 0, clamped))
            return;
        PersistSectionTab();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => MapZoomIn();

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => MapZoomOut();

    /// <inheritdoc />
    public void MapZoomIn() => ApplyMapZoom(zoomIn: true);

    /// <inheritdoc />
    public void MapZoomOut() => ApplyMapZoom(zoomIn: false);

    private void ApplyMapZoom(bool zoomIn)
    {
        if (ZoomScale == null || ZoomPan == null)
            return;
        var focus = new Point(ZoomScale.CenterX, ZoomScale.CenterY);
        if (!MapZoomInteractions.TryApplyZoomStep(ZoomScale, ZoomPan, zoomIn, focus))
            return;
        PersistSectionTab();
    }

    private void DesignLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _mapEditor?.OnMouseDownForMapPan(sender, e);
        DesignLayer_MapMouseDown(sender, e);
    }

    private void DesignLayer_MouseUp(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseUpForMapPan(sender, e);

    private void DesignLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseLeftButtonDown(sender, e);

    private void DesignLayer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) =>
        _mapEditor?.OnMouseMove(sender, e);

    private void DesignLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        _mapEditor?.OnMouseLeftButtonUp(sender, e);

    private void DesignLayer_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        _mapEditor?.OnMouseLeave(sender, e);

    private void DesignLayer_MapMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || e.ChangedButton != MouseButton.Left || _currentTool != MapCanvasEditorTool.PanZoom)
            return;
        ResetMapView();
        e.Handled = true;
    }

    private void CartographyOptions_Changed(object sender, RoutedEventArgs e)
    {
        Redraw();
        PersistSectionTab();
    }

    private PlanCanvasDrawOptions CurrentDrawOptions()
    {
        var intensity = CartographicIntensityParser.Parse(AppUiSettingsStore.LoadOrDefault().CartographicIntensity);
        return PlanCanvasDrawOptions.ForSection(
            StationNamesCheck?.IsChecked == true,
            CartographyOverlayCheck?.IsChecked != false,
            VisualizationMode,
            intensity,
            _surveyPickHighlight);
    }

    private void OnExternalStationSelected(object? sender, SurveyStationSelectionEventArgs e)
    {
        if (string.Equals(e.Source, "Section", StringComparison.OrdinalIgnoreCase))
            return;
        _surveyPickHighlight = new SurveyMapPickHighlight(false, e.StationName.Trim(), null);
        Redraw();
    }

    private void ResetView_Click(object sender, RoutedEventArgs e) => ResetMapView();

    /// <inheritdoc />
    public void ResetMapView()
    {
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        Redraw();
        PersistSectionTab();
    }

    /// <inheritdoc />
    public void ClearMapSelectionAndRedraw() => Redraw();

    /// <inheritdoc />
    public void ApplyMapEditorTool(MapCanvasEditorTool tool)
    {
        _applyingSettings = true;
        try
        {
            _currentTool = tool;
            if (EditorToolPan != null)
                EditorToolPan.IsChecked = _currentTool == MapCanvasEditorTool.PanZoom;
            if (EditorToolSelect != null)
                EditorToolSelect.IsChecked = _currentTool == MapCanvasEditorTool.Select;
            if (EditorToolDraw != null)
                EditorToolDraw.IsChecked = _currentTool == MapCanvasEditorTool.DrawFreehand;
            if (EditorToolSymbol != null)
                EditorToolSymbol.IsChecked = _currentTool == MapCanvasEditorTool.PlaceSymbol;
        }
        finally
        {
            _applyingSettings = false;
        }

        if (_currentTool == MapCanvasEditorTool.PlaceSymbol)
            EnsureSymbolPaletteHasSelection();
        SyncSymbolPaletteEnabled();
        PersistSectionTab();
    }

    private void PrintSection_Click(object sender, RoutedEventArgs e)
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show("Select a project.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var underlaysPrint = PlanMapUnderlayLoader.TryLoadRasterUnderlays(p, ZipPath, MapRows, MapInventory);
        var scene = SectionSceneBuilder.TryBuild(p, VisualizationMode);
        var hi = PrintHiContrastCheck.IsChecked == true;
        if (scene == null)
        {
            if (underlaysPrint.Count == 0)
            {
                MessageBox.Show(
                    "No section data to print and no resolvable map image for a raster-only preview.",
                    "Print",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            SectionCanvasRenderer.DrawRasterUnderlaysOnly(
                SurveyCanvas,
                hi,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlaysPrint,
                "section");
        }
        else
            SectionCanvasRenderer.Draw(
                scene,
                SurveyCanvas,
                hi,
                SurveyCanvas.Width,
                SurveyCanvas.Height,
                underlaysPrint,
                p,
                ZipPath,
                CurrentDrawOptions());
        var pd = new PrintDialog();
        try
        {
            pd.PrintTicket.PageOrientation = PageOrientation.Landscape;
        }
        catch { /* ignore */ }

        if (pd.ShowDialog() != true)
        {
            Redraw();
            return;
        }

        var pw = pd.PrintableAreaWidth;
        var ph = pd.PrintableAreaHeight;
        if (pw <= 0 || ph <= 0)
        {
            MessageBox.Show("Invalid printable area.", "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
            Redraw();
            return;
        }

        if (MapZoomRoot == null)
        {
            Redraw();
            return;
        }

        MapZoomRoot.Measure(new Size(MapZoomRoot.Width, MapZoomRoot.Height));
        MapZoomRoot.Arrange(new Rect(0, 0, MapZoomRoot.Width, MapZoomRoot.Height));
        MapZoomRoot.UpdateLayout();
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(MapZoomRoot.Width),
            (int)Math.Ceiling(MapZoomRoot.Height),
            96,
            96,
            PixelFormats.Pbgra32);
        rtb.Render(MapZoomRoot);
        var stack = new StackPanel { Background = Brushes.White, Width = pw };
        var titleSuffix = scene == null ? " — map preview (no survey geometry)" : "";
        stack.Children.Add(new TextBlock
        {
            Text = $"{p.Name}  ·  {p.Date}  ·  Section (survey m, CAVE AI PRO){titleSuffix}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(24, 18, 24, 10),
            TextWrapping = TextWrapping.Wrap,
        });
        var vb = new Viewbox { Width = pw - 48, Height = Math.Max(120, ph - 72), Margin = new Thickness(24, 0, 24, 24), Stretch = Stretch.Uniform };
        vb.Child = new Image { Source = rtb };
        stack.Children.Add(vb);
        stack.Measure(new Size(pw, ph));
        stack.Arrange(new Rect(0, 0, pw, ph));
        stack.UpdateLayout();
        try
        {
            pd.PrintVisual(stack, $"CAVE AI PRO — {p.Name} section");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ZoomPan.X = ZoomPan.Y = 0;
            ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
            Redraw();
        }
    }
}
