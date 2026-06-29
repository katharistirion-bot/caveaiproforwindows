using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.ViewModels;

/// <summary>
/// Serializes Sketch Editor session ink (design layer) onto
/// <see cref="CaveProjectDocument"/> for save-back to Android-compatible JSON.
/// Also owns undo/redo history for design-layer edits.
/// </summary>
public partial class SketchEditorViewModel : ObservableObject
{
    private readonly SketchEditorPersistenceHost _host;
    private readonly SketchEditorHistory _history = new();

    public SketchEditorViewModel(SketchEditorPersistenceHost host)
    {
        _host = host;
        _history.Changed += OnHistoryChanged;
    }

    public SketchEditorHistory History => _history;

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    /// <summary>Raised after undo/redo availability changes or history is cleared.</summary>
    public event Action? EditStateChanged;

    public void ClearHistory()
    {
        _history.Clear();
        OnHistoryChanged();
    }

    public void RecordInkAdded(UIElement element)
    {
        _history.PushAdded(element);
    }

    public void RecordInkRemoved(UIElement element, int index)
    {
        _history.PushRemoved(element, index);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        var canvas = _host.GetDesignLayer();
        if (canvas == null)
            return;
        if (!_history.TryUndo(canvas))
            return;

        TryPersistSessionToProject();
        EditStateChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        var canvas = _host.GetDesignLayer();
        if (canvas == null)
            return;
        if (!_history.TryRedo(canvas))
            return;

        TryPersistSessionToProject();
        EditStateChanged?.Invoke();
    }

    /// <summary>Writes design-layer strokes/symbols to <c>mapObjects</c> on the project document.</summary>
    public bool TryPersistSessionToProject(CaveProjectDocument? projectOverride = null)
    {
        var project = projectOverride ?? _host.GetProject();
        if (project == null || !_host.IsSurveyLayoutReady())
            return false;

        var layout = _host.GetSurveyLayout();
        var designLayer = _host.GetDesignLayer();
        return DesignLayerMapObjectsSerializer.TryApplyDesignLayerToProject(project, designLayer, layout);
    }

    /// <summary>Preview JSON array length without mutating the project (for diagnostics).</summary>
    public int CountDesignLayerMapObjectEntries()
    {
        if (!_host.IsSurveyLayoutReady())
            return 0;
        var el = DesignLayerMapObjectsSerializer.SerializeDesignLayer(
            _host.GetDesignLayer(),
            _host.GetSurveyLayout());
        return el.ValueKind == System.Text.Json.JsonValueKind.Array ? el.GetArrayLength() : 0;
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        EditStateChanged?.Invoke();
    }
}
