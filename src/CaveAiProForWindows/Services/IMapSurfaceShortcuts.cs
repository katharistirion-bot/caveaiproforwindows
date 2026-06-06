namespace CaveAiProForWindows.Services;

/// <summary>Plan / sketch map surfaces that support global keyboard shortcuts from the shell window.</summary>
public interface IMapSurfaceShortcuts
{
    void ClearMapSelectionAndRedraw();

    void ApplyMapEditorTool(MapCanvasEditorTool tool);

    void ResetMapView();

    /// <summary>Toolbar / keyboard zoom in (same factor as mouse wheel up).</summary>
    void MapZoomIn();

    /// <summary>Toolbar / keyboard zoom out (same factor as mouse wheel down).</summary>
    void MapZoomOut();

    /// <summary>Undo last sketch design-layer edit (Sketch Editor only).</summary>
    void UndoSketchEdit();

    /// <summary>Redo last undone sketch edit (Sketch Editor only).</summary>
    void RedoSketchEdit();

    /// <summary>Delete selected sketch ink or survey selection (Sketch Editor ink when selected).</summary>
    bool TryDeleteSelectedInk();
}
