using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Services.SketchAssist;

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
    private Rectangle? _inkSelectionFrame;
    private UIElement? _selectedInk;
    private UIElement? _lastErasedInk;
    private bool _isErasing;

    /// <summary>Invoked when the user finishes a freehand stroke or places a symbol stamp.</summary>
    public Action? DesignLayerModified { get; set; }

    /// <summary>Invoked after a new stroke or symbol is committed (for undo history).</summary>
    public Action<UIElement>? InkAdded { get; set; }

    /// <summary>Invoked after ink is removed (element, index before removal).</summary>
    public Action<UIElement, int>? InkRemoved { get; set; }

    public UIElement? SelectedInk => _selectedInk;

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

    /// <summary>Left: pan in Pan mode, else select / freehand / symbol / erase.</summary>
    public void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var tool = _getTool();
        if (tool == MapCanvasEditorTool.PanZoom)
        {
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
                if (TrySelectInkAt(pt))
                {
                    e.Handled = true;
                    break;
                }

                ClearInkSelection();
                EnsureSelectMarker();
                Canvas.SetLeft(_selectMarker!, pt.X - 6);
                Canvas.SetTop(_selectMarker!, pt.Y - 6);
                _selectMarker!.Visibility = Visibility.Visible;
                e.Handled = true;
                break;
            case MapCanvasEditorTool.DrawFreehand:
                ClearInkSelection();
                _activeDraw = new Polyline
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = SketchStrokeStyleDefaults.DefaultStrokeWidthPx,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Tag = DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx),
                };
                _activeDraw.Points.Add(pt);
                _designCanvas.Children.Add(_activeDraw);
                _designCanvas.CaptureMouse();
                e.Handled = true;
                break;
            case MapCanvasEditorTool.PlaceSymbol:
                ClearInkSelection();
                StampSketchSymbol(pt);
                e.Handled = true;
                break;
            case MapCanvasEditorTool.Erase:
                _isErasing = true;
                _lastErasedInk = null;
                TryEraseAt(pt);
                _designCanvas.CaptureMouse();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Pan drag, extend freehand stroke, or drag-erase.</summary>
    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var now = e.GetPosition(_scrollHost);
            _pan.X = _panStartX + (now.X - _panMouseStart.X);
            _pan.Y = _panStartY + (now.Y - _panMouseStart.Y);
            return;
        }

        if (_getTool() == MapCanvasEditorTool.Erase && _isErasing && e.LeftButton == MouseButtonState.Pressed)
        {
            TryEraseAt(e.GetPosition(_designCanvas));
            return;
        }

        if (_activeDraw == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        var pt = e.GetPosition(_designCanvas);
        _activeDraw.Points.Add(pt);
    }

    /// <summary>Ends left-drag pan, freehand stroke, or erase drag.</summary>
    public void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && _panButton == MouseButton.Left)
            EndPan();

        if (_isErasing && e.LeftButton == MouseButtonState.Released)
        {
            _isErasing = false;
            _lastErasedInk = null;
            if (_designCanvas.IsMouseCaptured)
                _designCanvas.ReleaseMouseCapture();
        }

        if (_activeDraw != null && e.LeftButton == MouseButtonState.Released)
        {
            _designCanvas.ReleaseMouseCapture();
            if (_activeDraw.Points.Count >= 2)
            {
                InkAdded?.Invoke(_activeDraw);
                DesignLayerModified?.Invoke();
            }
            else
            {
                _designCanvas.Children.Remove(_activeDraw);
            }

            _activeDraw = null;
        }
    }

    /// <summary>Ends middle-drag pan — wire from <see cref="UIElement.MouseUp"/>.</summary>
    public void OnMouseUpForMapPan(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && _panButton == MouseButton.Middle && e.ChangedButton == MouseButton.Middle)
            EndPan();
    }

    public void OnMouseLeave(object sender, MouseEventArgs e)
    {
        EndPan();
        if (_isErasing)
        {
            _isErasing = false;
            _lastErasedInk = null;
            if (_designCanvas.IsMouseCaptured)
                _designCanvas.ReleaseMouseCapture();
        }
    }

    public bool TrySelectInkAt(Point canvasPoint, double toleranceDip = 10)
    {
        var hit = DesignLayerInkHitTest.FindTopmostHit(_designCanvas, canvasPoint, toleranceDip);
        if (hit == null)
            return false;

        SelectInk(hit);
        return true;
    }

    public bool DeleteSelectedInk() =>
        _selectedInk != null && RemoveInkElement(_selectedInk);

    public bool RemoveInkElement(UIElement element)
    {
        if (!DesignLayerInkHitTest.IsEditableInk(element))
            return false;

        var idx = _designCanvas.Children.IndexOf(element);
        if (idx < 0)
            return false;

        if (ReferenceEquals(_selectedInk, element))
            ClearInkSelection();

        _designCanvas.Children.Remove(element);
        InkRemoved?.Invoke(element, idx);
        DesignLayerModified?.Invoke();
        return true;
    }

    public void ClearInkSelection()
    {
        _selectedInk = null;
        if (_inkSelectionFrame != null)
            _inkSelectionFrame.Visibility = Visibility.Collapsed;
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
        _inkSelectionFrame = null;
        _selectedInk = null;
        _activeDraw = null;
        _isPanning = false;
        _isErasing = false;
        _lastErasedInk = null;
    }

    private void SelectInk(UIElement element)
    {
        ClearTransientSelectHighlight();
        _selectedInk = element;
        EnsureInkSelectionFrame();
        UpdateInkSelectionFrame(element);
        _inkSelectionFrame!.Visibility = Visibility.Visible;
    }

    private void TryEraseAt(Point pt)
    {
        var hit = DesignLayerInkHitTest.FindTopmostHit(_designCanvas, pt);
        if (hit == null || ReferenceEquals(hit, _lastErasedInk))
            return;

        _lastErasedInk = hit;
        RemoveInkElement(hit);
    }

    private void EnsureInkSelectionFrame()
    {
        if (_inkSelectionFrame != null)
            return;

        _inkSelectionFrame = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x38, 0xB2, 0xAC)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Tag = DesignLayerInkHitTest.SelectionFrameTag,
        };
        _designCanvas.Children.Add(_inkSelectionFrame);
    }

    private void UpdateInkSelectionFrame(UIElement element)
    {
        if (_inkSelectionFrame == null)
            return;

        var bounds = DesignLayerInkHitTest.GetInkBounds(element);
        if (bounds.IsEmpty)
        {
            _inkSelectionFrame.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(_inkSelectionFrame, bounds.X);
        Canvas.SetTop(_inkSelectionFrame, bounds.Y);
        _inkSelectionFrame.Width = bounds.Width;
        _inkSelectionFrame.Height = bounds.Height;
    }

    private void StartPan(MouseButtonEventArgs e, MouseButton button)
    {
        _isPanning = true;
        _panButton = button;
        _panMouseStart = e.GetPosition(_scrollHost);
        _panStartX = _pan.X;
        _panStartY = _pan.Y;
        _designCanvas.CaptureMouse();
    }

    private void EndPan()
    {
        if (!_isPanning)
            return;
        _isPanning = false;
        if (_designCanvas.IsMouseCaptured && _activeDraw == null && !_isErasing)
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
            Tag = DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx),
        };
        Canvas.SetLeft(vb, anchorCenter.X - half);
        Canvas.SetTop(vb, anchorCenter.Y - half);
        _designCanvas.Children.Add(vb);
        InkAdded?.Invoke(vb);
        DesignLayerModified?.Invoke();
    }
}
