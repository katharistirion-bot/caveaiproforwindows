using System;
using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class SectionView : System.Windows.Controls.UserControl
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
        v.Redraw();
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

    private bool _isPanning;
    private System.Windows.Point _panMouseStart;
    private double _panStartX;
    private double _panStartY;

    public SectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed += OnSurveyCanvasThemeChanged;
        Redraw();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SurveyCanvasTheme.Changed -= OnSurveyCanvasThemeChanged;
        UnwireMapRows(MapRows);
        UnwireMapInventory(MapInventory);
    }

    private void OnSurveyCanvasThemeChanged() =>
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Redraw));

    private void Redraw()
    {
        if (DrawingCanvas == null)
            return;

        DrawingCanvas.Children.Clear();
        var p = Project;
        if (p == null)
        {
            AddMessage("Select a project from the list.");
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
                    ZoomScale.CenterX = DrawingCanvas.Width / 2;
                    ZoomScale.CenterY = DrawingCanvas.Height / 2;
                    SectionCanvasRenderer.DrawRasterUnderlaysOnly(
                        DrawingCanvas,
                        highContrast: false,
                        DrawingCanvas.Width,
                        DrawingCanvas.Height,
                        underlays,
                        "section");
                    return;
                }

                AddMessage(
                    "No section vectors/sketches (viewMode 1) or traverse data for this project. "
                    + "If cartography paths resolve to PNG/JPEG/WebP/TIFF (ZIP open, or .zip next to exported data.json), a raster underlay can still appear here.");
                return;
            }

            ZoomScale.CenterX = DrawingCanvas.Width / 2;
            ZoomScale.CenterY = DrawingCanvas.Height / 2;
            SectionCanvasRenderer.Draw(
                scene,
                DrawingCanvas,
                highContrast: false,
                DrawingCanvas.Width,
                DrawingCanvas.Height,
                underlays,
                p,
                ZipPath,
                CurrentDrawOptions());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SectionView] Redraw failed: {ex}");
            DrawingCanvas.Children.Clear();
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
        DrawingCanvas.Children.Add(tb);
    }

    private void HostScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        e.Handled = true;
        var z = e.Delta > 0 ? 1.12 : 1 / 1.12;
        var nx = Math.Clamp(ZoomScale.ScaleX * z, 0.12, 12.0);
        ZoomScale.ScaleX = nx;
        ZoomScale.ScaleY = nx;
    }

    private void DrawingCanvas_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed)
            return;
        _isPanning = true;
        _panMouseStart = e.GetPosition(HostScroll);
        _panStartX = ZoomPan.X;
        _panStartY = ZoomPan.Y;
        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void DrawingCanvas_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPanning)
            return;
        var now = e.GetPosition(HostScroll);
        ZoomPan.X = _panStartX + (now.X - _panMouseStart.X);
        ZoomPan.Y = _panStartY + (now.Y - _panMouseStart.Y);
    }

    private void DrawingCanvas_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Released)
            return;
        EndPan();
    }

    private void DrawingCanvas_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => EndPan();

    private void EndPan()
    {
        if (!_isPanning)
            return;
        _isPanning = false;
        DrawingCanvas.ReleaseMouseCapture();
    }

    private void CartographyOptions_Changed(object sender, RoutedEventArgs e) => Redraw();

    private PlanCanvasDrawOptions CurrentDrawOptions() =>
        PlanCanvasDrawOptions.ForSection(
            StationNamesCheck?.IsChecked == true,
            CartographyOverlayCheck?.IsChecked != false,
            VisualizationMode);

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
        Redraw();
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
                DrawingCanvas,
                hi,
                DrawingCanvas.Width,
                DrawingCanvas.Height,
                underlaysPrint,
                "section");
        }
        else
            SectionCanvasRenderer.Draw(
                scene,
                DrawingCanvas,
                hi,
                DrawingCanvas.Width,
                DrawingCanvas.Height,
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

        DrawingCanvas.Measure(new Size(DrawingCanvas.Width, DrawingCanvas.Height));
        DrawingCanvas.Arrange(new Rect(0, 0, DrawingCanvas.Width, DrawingCanvas.Height));
        DrawingCanvas.UpdateLayout();
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(DrawingCanvas.Width),
            (int)Math.Ceiling(DrawingCanvas.Height),
            96,
            96,
            PixelFormats.Pbgra32);
        rtb.Render(DrawingCanvas);
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
