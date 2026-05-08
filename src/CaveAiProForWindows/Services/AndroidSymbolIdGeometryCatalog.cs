namespace CaveAiProForWindows.Services;

/// <summary>
/// Desktop companion to Android sketch-editor symbol IDs: maps exported <c>symbolId</c> / labels to frozen WPF
/// <see cref="Geometry"/> + inks (<see cref="SketchSymbolInk"/>). Extend <see cref="AndroidSketchSymbolKindMapper"/> /
/// <see cref="SketchSymbolDefinitions"/> when Android adds palette entries.
/// </summary>
public static class AndroidSymbolIdGeometryCatalog
{
    /// <summary>Single resolution step — avoids duplicate mapper work in callers.</summary>
    public static (SketchEditorSymbolKind Kind, SketchSymbolInk Ink) ResolveInk(string? symbolId, string? label, string? iconKey)
    {
        var kind = AndroidSketchSymbolKindMapper.Resolve(symbolId, label, iconKey);
        return (kind, SketchSymbolDefinitions.Get(kind));
    }
}
