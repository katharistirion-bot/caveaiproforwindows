using System.Windows;
using System.Windows.Media;

using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Resolves Android <see cref="SurveyStationGeometry.PlanMapSymbol"/> stamps to drawable ink — UIS catalog,
/// emoji quick-picks, hazard tokens, then legacy sketch stamps.
/// </summary>
public static class MapSymbolIconResolver
{
    public const string DangerDeltaSkull = "__DANGER_DELTA_SKULL__";
    public const string DifficultNarrowPassage = "__DIFFICULT_NARROW_PASSAGE__";

    public enum RenderMode
    {
        VectorPath,
        EmojiGlyph,
    }

    public readonly struct ResolvedMapSymbol
    {
        public RenderMode Mode { get; init; }
        public Geometry? Geometry { get; init; }
        public string? Emoji { get; init; }
        public Brush Stroke { get; init; }
        public Brush Fill { get; init; }
        public string DisplayLabel { get; init; }
        /// <summary>When true, geometry lives in normalized [-1,1] space (UIS). Otherwise 24×24 legacy stamp space.</summary>
        public bool NormalizedUisSpace { get; init; }
    }

    private static readonly Brush UisStroke = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
    private static readonly Brush UisFill = new SolidColorBrush(Color.FromArgb(0x55, 0x45, 0xA8, 0x9A));
    private static readonly Brush HazardStroke = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
    private static readonly Brush HazardFill = new SolidColorBrush(Color.FromArgb(0x55, 0xEF, 0x44, 0x44));

    private static readonly Dictionary<string, string> EmojiToUisSlug =
        new(StringComparer.Ordinal)
        {
            ["🌵"] = "stalactite",
            ["💧"] = "drip",
            ["🦴"] = "bones",
            ["🏛️"] = "bones",
            ["🧱"] = "boulder",
            ["🧗"] = "fixed_rope",
            ["🕳️"] = "pit",
            ["🦇"] = "guano",
            ["🌀"] = "air_draft",
            ["🪵"] = "wood_debris",
            ["🌊"] = "pool_water",
            ["💠"] = "stalactite",
            ["⛓"] = "fixed_rope",
        };

    static MapSymbolIconResolver()
    {
        UisStroke.Freeze();
        UisFill.Freeze();
        HazardStroke.Freeze();
        HazardFill.Freeze();
    }

    public static ResolvedMapSymbol Resolve(SurveyStationGeometry.PlanMapSymbol sym, bool highContrast)
    {
        foreach (var raw in new[] { sym.IconKey, sym.SymbolId, sym.Label })
        {
            if (TryResolveRaw(raw, highContrast, out var resolved))
                return resolved;
        }

        var kind = AndroidSketchSymbolKindMapper.Resolve(sym.SymbolId, sym.Label, sym.IconKey);
        var ink = SketchSymbolDefinitions.Get(kind);
        return new ResolvedMapSymbol
        {
            Mode = RenderMode.VectorPath,
            Geometry = ink.Geometry,
            Stroke = highContrast ? Brushes.White : ink.Stroke,
            Fill = highContrast ? Brushes.White : ink.Fill,
            DisplayLabel = FormatLegacyLabel(sym, kind),
            NormalizedUisSpace = false,
        };
    }

    public static string ExportLabel(SurveyStationGeometry.PlanMapSymbol sym)
    {
        foreach (var raw in new[] { sym.IconKey, sym.SymbolId, sym.Label })
        {
            if (TryExportLabel(raw, out var label))
                return label;
        }

        return sym.Label?.Trim() ?? sym.IconKey?.Trim() ?? sym.SymbolId?.Trim() ?? "Map symbol";
    }

    private static bool TryResolveRaw(string? raw, bool highContrast, out ResolvedMapSymbol resolved)
    {
        resolved = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();

        if (string.Equals(s, DangerDeltaSkull, StringComparison.Ordinal))
        {
            resolved = BuildHazardVector("Danger (hazard)", highContrast);
            return true;
        }

        if (string.Equals(s, DifficultNarrowPassage, StringComparison.Ordinal))
        {
            resolved = BuildPinchVector("Difficult narrow passage", highContrast);
            return true;
        }

        if (CaveMappingSymbolCatalog.TryParseIconKey(s, out var caveKind))
        {
            var geom = CaveMappingSymbolCatalog.GetGeometry(caveKind);
            resolved = new ResolvedMapSymbol
            {
                Mode = RenderMode.VectorPath,
                Geometry = geom,
                Stroke = highContrast ? Brushes.White : UisStroke,
                Fill = highContrast ? Brushes.White : UisFill,
                DisplayLabel = CaveMappingSymbolCatalog.GetLabel(caveKind),
                NormalizedUisSpace = false,
            };
            return true;
        }

        if (UisCaveSymbolGeometryCatalog.TryParseStoredIcon(s, out var slug) &&
            UisCaveSymbolGeometryCatalog.TryGetGeometry(slug, out var uisGeom))
        {
            UisCaveSymbolGeometryCatalog.TryGetLabel(slug, out var label);
            resolved = new ResolvedMapSymbol
            {
                Mode = RenderMode.VectorPath,
                Geometry = uisGeom,
                Stroke = highContrast ? Brushes.White : UisStroke,
                Fill = highContrast ? Brushes.White : UisFill,
                DisplayLabel = label,
                NormalizedUisSpace = true,
            };
            return true;
        }

        if (EmojiToUisSlug.TryGetValue(s, out slug) &&
            UisCaveSymbolGeometryCatalog.TryGetGeometry(slug, out uisGeom))
        {
            UisCaveSymbolGeometryCatalog.TryGetLabel(slug, out var label);
            resolved = new ResolvedMapSymbol
            {
                Mode = RenderMode.VectorPath,
                Geometry = uisGeom,
                Stroke = highContrast ? Brushes.White : UisStroke,
                Fill = highContrast ? Brushes.White : UisFill,
                DisplayLabel = label,
                NormalizedUisSpace = true,
            };
            return true;
        }

        if (LooksLikeEmoji(s))
        {
            resolved = new ResolvedMapSymbol
            {
                Mode = RenderMode.EmojiGlyph,
                Emoji = s,
                Stroke = Brushes.Transparent,
                Fill = Brushes.Transparent,
                DisplayLabel = s,
                NormalizedUisSpace = false,
            };
            return true;
        }

        return false;
    }

    private static bool TryExportLabel(string? raw, out string label)
    {
        label = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var s = raw.Trim();
        if (string.Equals(s, DangerDeltaSkull, StringComparison.Ordinal))
        {
            label = "Danger (hazard)";
            return true;
        }

        if (string.Equals(s, DifficultNarrowPassage, StringComparison.Ordinal))
        {
            label = "Difficult narrow passage";
            return true;
        }

        if (CaveMappingSymbolCatalog.TryParseIconKey(s, out var caveKind))
        {
            label = CaveMappingSymbolCatalog.GetLabel(caveKind);
            return true;
        }

        if (UisCaveSymbolGeometryCatalog.TryParseStoredIcon(s, out var slug) &&
            UisCaveSymbolGeometryCatalog.TryGetLabel(slug, out label))
            return true;

        if (EmojiToUisSlug.TryGetValue(s, out slug) &&
            UisCaveSymbolGeometryCatalog.TryGetLabel(slug, out label))
            return true;

        if (LooksLikeEmoji(s))
        {
            label = s;
            return true;
        }

        return false;
    }

    private static ResolvedMapSymbol BuildHazardVector(string label, bool highContrast)
    {
        var group = new GeometryGroup();
        var tri = new PathGeometry([
            new PathFigure(new Point(-0.55, 0.45), [new LineSegment(new Point(0.55, 0.45), true), new LineSegment(new Point(0, -0.55), true)], true),
        ]);
        group.Children.Add(tri);
        group.Children.Add(new LineGeometry(new Point(-0.15, 0.05), new Point(0.15, 0.05)));
        group.Children.Add(new LineGeometry(new Point(0, 0.15), new Point(0, 0.35)));
        group.Freeze();
        return new ResolvedMapSymbol
        {
            Mode = RenderMode.VectorPath,
            Geometry = group,
            Stroke = highContrast ? Brushes.White : HazardStroke,
            Fill = highContrast ? new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)) : HazardFill,
            DisplayLabel = label,
            NormalizedUisSpace = true,
        };
    }

    private static ResolvedMapSymbol BuildPinchVector(string label, bool highContrast)
    {
        var group = new GeometryGroup();
        group.Children.Add(new LineGeometry(new Point(-0.55, -0.15), new Point(0.55, -0.15)));
        group.Children.Add(new LineGeometry(new Point(-0.55, 0.15), new Point(0.55, 0.15)));
        group.Children.Add(new LineGeometry(new Point(-0.25, -0.45), new Point(-0.05, 0.45)));
        group.Children.Add(new LineGeometry(new Point(0.25, -0.45), new Point(0.05, 0.45)));
        group.Freeze();
        return new ResolvedMapSymbol
        {
            Mode = RenderMode.VectorPath,
            Geometry = group,
            Stroke = highContrast ? Brushes.White : UisStroke,
            Fill = Brushes.Transparent,
            DisplayLabel = label,
            NormalizedUisSpace = true,
        };
    }

    private static bool LooksLikeEmoji(string s) =>
        s.Length <= 8 && s.Any(ch => char.GetUnicodeCategory(ch) is System.Globalization.UnicodeCategory.OtherSymbol
            or System.Globalization.UnicodeCategory.Surrogate);

    private static string FormatLegacyLabel(SurveyStationGeometry.PlanMapSymbol sym, SketchEditorSymbolKind kind)
    {
        if (!string.IsNullOrWhiteSpace(sym.Label))
            return sym.Label.Trim();
        if (!string.IsNullOrWhiteSpace(sym.IconKey))
            return sym.IconKey.Trim();
        return kind.ToString();
    }
}
