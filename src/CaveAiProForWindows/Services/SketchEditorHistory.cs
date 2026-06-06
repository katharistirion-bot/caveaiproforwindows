using System.Windows;
using System.Windows.Controls;

namespace CaveAiProForWindows.Services;

/// <summary>Undo/redo stack for sketch design-layer ink (strokes and symbol stamps).</summary>
public sealed class SketchEditorHistory
{
    private readonly Stack<ISketchHistoryAction> _undo = new();
    private readonly Stack<ISketchHistoryAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    public void PushAdded(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _undo.Push(new AddInkAction(element));
        _redo.Clear();
        Changed?.Invoke();
    }

    public void PushRemoved(UIElement element, int index)
    {
        ArgumentNullException.ThrowIfNull(element);
        _undo.Push(new RemoveInkAction(element, index));
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool TryUndo(Canvas canvas)
    {
        if (!CanUndo)
            return false;
        var action = _undo.Pop();
        action.Undo(canvas);
        _redo.Push(action);
        Changed?.Invoke();
        return true;
    }

    public bool TryRedo(Canvas canvas)
    {
        if (!CanRedo)
            return false;
        var action = _redo.Pop();
        action.Redo(canvas);
        _undo.Push(action);
        Changed?.Invoke();
        return true;
    }

    private interface ISketchHistoryAction
    {
        void Undo(Canvas canvas);

        void Redo(Canvas canvas);
    }

    private sealed class AddInkAction : ISketchHistoryAction
    {
        private readonly UIElement _element;
        private int _index = -1;

        public AddInkAction(UIElement element) => _element = element;

        public void Undo(Canvas canvas)
        {
            _index = canvas.Children.IndexOf(_element);
            if (_index >= 0)
                canvas.Children.Remove(_element);
        }

        public void Redo(Canvas canvas)
        {
            if (canvas.Children.Contains(_element))
                return;
            if (_index >= 0 && _index <= canvas.Children.Count)
                canvas.Children.Insert(_index, _element);
            else
                canvas.Children.Add(_element);
        }
    }

    private sealed class RemoveInkAction : ISketchHistoryAction
    {
        private readonly UIElement _backupClone;
        private readonly int _index;
        private UIElement? _live;

        public RemoveInkAction(UIElement removed, int index)
        {
            _live = removed;
            _index = index;
            _backupClone = DesignLayerElementCloner.Clone(removed);
        }

        public void Undo(Canvas canvas)
        {
            if (_live != null && canvas.Children.Contains(_live))
                return;
            var insertAt = Math.Clamp(_index, 0, canvas.Children.Count);
            canvas.Children.Insert(insertAt, _backupClone);
            _live = _backupClone;
        }

        public void Redo(Canvas canvas)
        {
            if (_live == null)
                return;
            if (canvas.Children.Contains(_live))
                canvas.Children.Remove(_live);
        }
    }
}
