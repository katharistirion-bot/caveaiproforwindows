using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CaveAiProForWindows.Services;

/// <summary>
/// CAD-style map navigation: middle mouse (<see cref="UIElement.MouseDown"/>) always pans;
/// left button pans only in <see cref="MapCanvasEditorTool.PanZoom"/> via <see cref="UIElement.MouseLeftButtonDown"/>.
/// </summary>
public sealed class MapCanvasEditorController
{
    private readonly Canvas _designCanvas;
    private readonly ScrollViewer _scrollHost;
    private readonly TranslateTransform _pan;
    private readonly Func<MapCanvasEditorTool> _getTool;
    private readonly Func<SketchEditorSymbolKind> _getStampSymbol;

    private bool _isPanning;
    private MouseButton _panButton;
    private Point _panMouseStart;
    private double _panStartX;
    private double _panStartY;

    private Polyline? _activeDraw;
    private Ellipse? _selectMarker;

    public MapCanvasEditorController(
        Canvas designCanvas,
        ScrollViewer scrollHost,
        TranslateTransform pan,
        Func<MapCanvasEditorTool> getTool,
        Func<SketchEditorSymbolKind> getStampSymbol)
    {
        _designCanvas = designCanvas;
        _scrollHost = scrollHost;
        _pan = pan;
        _getTool = getTool;
        _getStampSymbol = getStampSymbol;
    }

    /// <summary>Middle-button pan — wire from <see cref="UIElement.MouseDown"/> (fires before <see cref="UIElement.MouseLeftButtonDown"/>).</summary>
    public void OnMouseDownForMapPan(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton != MouseButtonState.Pressed)
            return;
        StartPan(e, MouseButton.Middle);
        e.Handled = true;
    }

    /// <summary>Left: pan in Pan mode, else select / freehand / symbol — wire from <see cref="UIElement.MouseLeftButtonDown"/>.</summary>
    public void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var tool = _getTool();
        if (tool == MapCanvasEditorTool.PanZoom)
        {
            // Second press of a double-click is reserved for "reset view" on the map surface (handled in the view).
            if (e.ClickCount >= 2)
                return;
            StartPan(e, MouseButton.Left);
            e.Handled = true;
            return;
        }

        var pt = e.GetPosition(_designCanvas);

        switch (tool)
        {
            case MapCanvasEditorTool.Select:
                EnsureSelectMarker();
                Canvas.SetLeft(_selectMarker!, pt.X - 6);
                Canvas.SetTop(_selectMarker!, pt.Y - 6);
                _selectMarker!.Visibility = Visibility.Visible;
                e.Handled = true;
                break;
            case MapCanvasEditorTool.DrawFreehand:
                _activeDraw = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.5,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                _activeDraw.Points.Add(pt);
                _designCanvas.Children.Add(_activeDraw);
                _designCanvas.CaptureMouse();
                e.Handled = true;
                break;
            case MapCanvasEditorTool.PlaceSymbol:
                StampSketchSymbol(pt);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Pan drag or extend freehand stroke.</summary>
    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var now = e.GetPosition(_scrollHost);
            _pan.X = _panStartX + (now.X - _panMouseStart.X);
            _pan.Y = _panStartY + (now.Y - _panMouseStart.Y);
            return;
        }

        if (_activeDraw == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var pt = e.GetPosition(_designCanvas);
        _activeDraw.Points.Add(pt);
    }

    /// <summary>Ends left-drag pan or releases freehand stroke — wire from <see cref="UIElement.MouseLeftButtonUp"/>.</summary>
    public void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && _panButton == MouseButton.Left)
            EndPan();

        if (_activeDraw != null && e.LeftButton == MouseButtonState.Released)
        {
            _designCanvas.ReleaseMouseCapture();
            _activeDraw = null;
        }
    }

    /// <summary>Ends middle-drag pan — wire from <see cref="UIElement.MouseUp"/>.</summary>
    public void OnMouseUpForMapPan(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && _panButton == MouseButton.Middle && e.ChangedButton == MouseButton.Middle)
            EndPan();
    }

    public void OnMouseLeave(object sender, MouseEventArgs e) => EndPan();

    private void StartPan(MouseButtonEventArgs e, MouseButton button)
    {
        _isPanning = true;
        _panButton = button;
        _panMouseStart = e.GetPosition(_scrollHost);
        _panStartX = _pan.X;
        _panStartY = _pan.Y;
        _designCanvas.CaptureMouse();
    }

    public void EndPan()
    {
        if (!_isPanning)
            return;
        _isPanning = false;
        if (_designCanvas.IsMouseCaptured)
            _designCanvas.ReleaseMouseCapture();
    }

    private void EnsureSelectMarker()
    {
        if (_selectMarker != null)
            return;
        _selectMarker = new Ellipse
        {
            Width = 12,
            Height = 12,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xD7, 0x2E)),
            StrokeThickness = 2,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _designCanvas.Children.Add(_selectMarker);
    }

    private void StampSketchSymbol(Point anchorCenter)
    {
        var ink = SketchSymbolDefinitions.Get(_getStampSymbol());
        var path = new Path
        {
            Data = ink.Geometry,
            Stroke = ink.Stroke,
            Fill = ink.Fill,
            StrokeThickness = 1.25,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        var half = SketchSymbolDefinitions.StampDisplaySize * 0.5;
        var vb = new Viewbox
        {
            Width = SketchSymbolDefinitions.StampDisplaySize,
            Height = SketchSymbolDefinitions.StampDisplaySize,
            Stretch = Stretch.Uniform,
            Child = path,
        };
        Canvas.SetLeft(vb, anchorCenter.X - half);
        Canvas.SetTop(vb, anchorCenter.Y - half);
        _designCanvas.Children.Add(vb);
    }

    public void ClearTransientSelectHighlight()
    {
        if (_selectMarker != null)
            _selectMarker.Visibility = Visibility.Collapsed;
    }

    /// <summary>Call after <c>DesignCanvas.Children.Clear()</c> so internal references are dropped.</summary>
    public void OnDesignLayerCleared()
    {
        _selectMarker = null;
        _activeDraw = null;
        _isPanning = false;
    }
}
