using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.SketchAssist;

public sealed class ProceduralSketchOptions
{
    public bool IncludeWallOutlines { get; init; } = true;

    public bool IncludeMapSymbols { get; init; } = true;

    public bool IncludeFieldCatalogPins { get; init; } = true;

    /// <summary>When true, adds LRUD / radial splay segments as dashed procedural strokes.</summary>
    public bool IncludeSplayOutlines { get; init; }
}

public sealed class ProceduralSketchResult
{
    public List<SketchStrokeModel> Strokes { get; init; } = new();

    public List<SketchSymbolStampModel> SymbolStamps { get; init; } = new();

    public bool HasContent => Strokes.Count > 0 || SymbolStamps.Count > 0;
}

/// <summary>
/// Derives editable design-layer geometry from traverse LRUD walls, map symbols, and field catalog pins.
/// </summary>
public static class ProceduralSketchGenerator
{
    public static bool TryGenerate(
        CaveProjectDocument project,
        ProceduralSketchOptions? options,
        out ProceduralSketchResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new ProceduralSketchOptions();
        result = new ProceduralSketchResult();

        var scene = PlanSceneBuilder.TryBuild(
            project,
            SurveyStationGeometry.AndroidViewModePlan,
            SurveyVisualizationMode.Standard);
        if (scene == null)
            return false;

        if (options.IncludeWallOutlines)
        {
            foreach (var wall in scene.WallPolylines)
            {
                if (wall.Points.Count < 2)
                    continue;
                // Only LRUD-derived ribbons — skip persisted mapObjects/sketch ink already on the design layer.
                if (!SurveyStationGeometry.IsLrudDerivedWallType(wall.Type))
                    continue;
                result.Strokes.Add(new SketchStrokeModel
                {
                    Closed = wall.Closed,
                    Source = SketchStrokeStyleDefaults.ProceduralSource,
                    Metadata = DesignLayerInkMetadata.ForProceduralWall(),
                    Points = wall.Points
                        .Select(p => (p.x, p.y))
                        .ToList(),
                });
            }
        }

        if (options.IncludeMapSymbols)
        {
            foreach (var sym in scene.Symbols)
            {
                var kind = AndroidSketchSymbolKindMapper.Resolve(sym.SymbolId, sym.Label, sym.IconKey);
                result.SymbolStamps.Add(new SketchSymbolStampModel
                {
                    SurveyX = sym.X,
                    SurveyY = sym.Y,
                    Kind = kind,
                });
            }
        }

        if (options.IncludeFieldCatalogPins)
        {
            foreach (var pin in scene.FieldCatalogPins)
            {
                var kind = ResolveCatalogPinKind(pin);
                result.SymbolStamps.Add(new SketchSymbolStampModel
                {
                    SurveyX = pin.X,
                    SurveyY = pin.Y,
                    Kind = kind,
                });
            }
        }

        if (options.IncludeSplayOutlines)
        {
            foreach (var (x1, y1, x2, y2) in scene.SplaySegments)
            {
                result.Strokes.Add(new SketchStrokeModel
                {
                    Source = SketchStrokeStyleDefaults.ProceduralSource,
                    Metadata = DesignLayerInkMetadata.ForProceduralSplay(),
                    Points = [(x1, y1), (x2, y2)],
                });
            }
        }

        return result.HasContent;
    }

    private static SketchEditorSymbolKind ResolveCatalogPinKind(FieldCatalogMapPin pin)
    {
        if (AndroidSketchSymbolKindMapper.Resolve(null, pin.CategoryLabel, pin.ScientificName) is var k
            && k != SketchEditorSymbolKind.StalactiteSpeleothem)
            return k;
        return AndroidSketchSymbolKindMapper.Resolve(null, pin.Title, pin.ScientificName);
    }
}
