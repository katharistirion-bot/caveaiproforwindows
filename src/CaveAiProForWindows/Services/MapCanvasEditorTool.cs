namespace CaveAiProForWindows.Services;

/// <summary>Interactive map mode for plan/section canvases (wheel zoom, middle or left-drag pan in Pan mode).</summary>
public enum MapCanvasEditorTool
{
    /// <summary>Left- or middle-drag pan, mouse-wheel zoom — default.</summary>
    PanZoom,

    /// <summary>Click to place a highlight marker (foundation for station / segment hit testing).</summary>
    Select,

    /// <summary>Left-drag freehand polyline on the design layer.</summary>
    DrawFreehand,

    /// <summary>Left-drag straight segment (passage wall / guide line).</summary>
    DrawLine,

    /// <summary>Left-click to drop a simple cave-symbol marker.</summary>
    PlaceSymbol,

    /// <summary>Click or drag to erase user ink (stroke-level hit testing).</summary>
    Erase,
}
