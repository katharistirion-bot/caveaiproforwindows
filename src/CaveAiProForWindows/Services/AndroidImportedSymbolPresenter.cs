using System.Windows;
using System.Windows.Controls;

namespace CaveAiProForWindows.Services;

/// <summary>Syncs Android-imported <see cref="SurveyStationGeometry.PlanMapSymbol"/> stamps onto a <see cref="Canvas"/> (design layer).</summary>
public static class AndroidImportedSymbolPresenter
{
    public const string ImportedChildTag = "__caveAiAndroidMapSymbol";

    public static void ClearImported(Canvas? designLayer)
    {
        if (designLayer == null)
            return;
        for (var i = designLayer.Children.Count - 1; i >= 0; i--)
        {
            if (designLayer.Children[i] is FrameworkElement fe && Equals(fe.Tag, ImportedChildTag))
                designLayer.Children.RemoveAt(i);
        }
    }

    /// <summary>Removes prior imported visuals and adds current symbols below user ink (insert at index 0).</summary>
    public static void SyncDesignLayer(
        Canvas? designLayer,
        PlanScene scene,
        PlanCanvasSurveyLayout layout,
        bool highContrast = false)
    {
        if (designLayer == null)
            return;
        ClearImported(designLayer);
        if (scene.Symbols.Count == 0)
            return;

        foreach (var sym in scene.Symbols)
        {
            var el = AndroidMapSymbolVisualFactory.CreateVisual(sym, layout, highContrast);
            if (el is FrameworkElement fe)
                fe.Tag = ImportedChildTag;
            Panel.SetZIndex(el, -20);
            designLayer.Children.Insert(0, el);
        }
    }
}
