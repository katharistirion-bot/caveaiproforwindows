using System.Collections;
using System.Collections.Specialized;
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

public partial class PlanView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(PlanView),
        new PropertyMetadata(null, (d, _) => ((PlanView)d).Redraw()));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(PlanView),
        new PropertyMetadata(null, (d, _) => ((PlanView)d).Redraw()));

    public static readonly DependencyProperty MapRowsProperty = DependencyProperty.Register(
        nameof(MapRows),
        typeof(IEnumerable),
        typeof(PlanView),
        new PropertyMetadata(null, OnMapRowsChanged));

    public static readonly DependencyProperty MapInventoryProperty = DependencyProperty.Register(
        nameof(MapInventory),
        typeof(IEnumerable),
        typeof(PlanView),
        new PropertyMetadata(null, OnMapInventoryChanged));

    private static void OnMapRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (PlanView)d;
        v.UnwireMapRows(e.OldValue);
        v.WireMapRows(e.NewValue);
        v.Redraw();
    }

    private static void OnMapInventoryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var v = (PlanView)d;
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

    /// <summary>Open backup .zip path (if any) so map assets inside the archive can be extracted for the plan underlay.</summary>
    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    /// <summary>All Maps-tab rows (includes standalone files) for resolving raster underlays.</summary>
    public IEnumerable? MapRows
    {
        get => (IEnumerable?)GetValue(MapRowsProperty);
        set => SetValue(MapRowsProperty, value);
    }

    /// <summary>Rows from Android <c>map_inventory.json</c> when a backup ZIP was opened.</summary>
    public IEnumerable? MapInventory
    {
        get => (IEnumerable?)GetValue(MapInventoryProperty);
        set => SetValue(MapInventoryProperty, value);
    }

    private bool _isPanning;
    private System.Windows.Point _panMouseStart;
    private double _panStartX;
    private double _panStartY;

    public PlanView()
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
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Redraw));

    private void Redraw()
    {
        // During InitializeComponent(), CheckBox IsChecked can fire before named fields (e.g. DrawingCanvas) exist.
        if (DrawingCanvas == null)
            return;

        DrawingCanvas.Children.Clear();
        var p = Project;
        if (p == null)
        {
            AddMessage("Select a project from the list.");
            return;
        }

        var underlay = PlanMapUnderlayLoader.TryLoadRasterUnderlay(p, ZipPath, MapRows, MapInventory);
        var scene = PlanSceneBuilder.TryBuild(p, SurveyStationGeometry.AndroidViewModePlan);
        if (scene == null)
        {
            if (underlay != null)
            {
                ZoomScale.CenterX = DrawingCanvas.Width / 2;
                ZoomScale.CenterY = DrawingCanvas.Height / 2;
                PlanCanvasRenderer.DrawRasterUnderlayOnly(
                    DrawingCanvas,
                    highContrast: false,
                    DrawingCanvas.Width,
                    DrawingCanvas.Height,
                    underlay,
                    "plan");
                return;
            }

            AddMessage(
                "No plan data yet — add traverse shots (to ≠ \"-\") and/or wall sketches or vectors in CaveAI Pro (Android), then re-export. "
                + "If the Maps tab lists PNG/JPEG/WebP/TIFF paths that resolve (inside an open .zip, or keep the original .zip next to an exported data.json in the same folder), a map preview appears here even without traverse.");
            return;
        }

        ZoomScale.CenterX = DrawingCanvas.Width / 2;
        ZoomScale.CenterY = DrawingCanvas.Height / 2;
        PlanCanvasRenderer.Draw(
            scene,
            DrawingCanvas,
            highContrast: false,
            DrawingCanvas.Width,
            DrawingCanvas.Height,
            underlay,
            CurrentDrawOptions());
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
        var dx = now.X - _panMouseStart.X;
        var dy = now.Y - _panMouseStart.Y;
        ZoomPan.X = _panStartX + dx;
        ZoomPan.Y = _panStartY + dy;
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
        PlanCanvasDrawOptions.ForPlan(
            StationNamesCheck?.IsChecked == true,
            CartographyOverlayCheck?.IsChecked != false);

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ZoomPan.X = 0;
        ZoomPan.Y = 0;
        ZoomScale.ScaleX = 1;
        ZoomScale.ScaleY = 1;
    }

    private void PrintPlan_Click(object sender, RoutedEventArgs e)
    {
        var p = Project;
        if (p == null)
        {
            MessageBox.Show(
                "Select a project from the list on the left.",
                "Print plan",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var underlayPrint = PlanMapUnderlayLoader.TryLoadRasterUnderlay(p, ZipPath, MapRows, MapInventory);
        var scene = PlanSceneBuilder.TryBuild(p, SurveyStationGeometry.AndroidViewModePlan);
        var hi = PrintHiContrastCheck.IsChecked == true;
        if (scene == null)
        {
            if (underlayPrint == null)
            {
                MessageBox.Show(
                    "No plan data to print (add traverse shots and/or sketches or vectors), and no resolvable map image for a raster-only preview.",
                    "Print plan",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            PlanCanvasRenderer.DrawRasterUnderlayOnly(
                DrawingCanvas,
                hi,
                DrawingCanvas.Width,
                DrawingCanvas.Height,
                underlayPrint,
                "plan");
        }
        else
            PlanCanvasRenderer.Draw(
                scene,
                DrawingCanvas,
                hi,
                DrawingCanvas.Width,
                DrawingCanvas.Height,
                underlayPrint,
                CurrentDrawOptions());

        var pd = new PrintDialog();
        try
        {
            pd.PrintTicket.PageOrientation = PageOrientation.Landscape;
        }
        catch
        {
            /* ignore */
        }

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

        var pxW = (int)Math.Max(1, Math.Ceiling(DrawingCanvas.Width));
        var pxH = (int)Math.Max(1, Math.Ceiling(DrawingCanvas.Height));
        var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(DrawingCanvas);

        var stack = new StackPanel { Background = Brushes.White, Width = pw };
        var titleSuffix = scene == null ? " — map preview (no survey geometry)" : "";
        var title = new TextBlock
        {
            Text = $"{p.Name}  ·  {p.Date}  ·  Plan (survey m, CAVE AI PRO){titleSuffix}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(24, 18, 24, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(title);

        var vb = new Viewbox
        {
            Width = pw - 48,
            Height = Math.Max(120, ph - 72),
            Margin = new Thickness(24, 0, 24, 24),
            Stretch = Stretch.Uniform,
        };
        vb.Child = new Image { Source = rtb, SnapsToDevicePixels = true };
        stack.Children.Add(vb);

        stack.Measure(new Size(pw, ph));
        stack.Arrange(new Rect(0, 0, pw, ph));
        stack.UpdateLayout();

        try
        {
            pd.PrintVisual(stack, $"CAVE AI PRO — {p.Name} plan");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Print error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ZoomPan.X = 0;
            ZoomPan.Y = 0;
            ZoomScale.ScaleX = ZoomScale.ScaleY = 1;
            Redraw();
        }
    }
}
