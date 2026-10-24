using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>One Sketch Editor section stroke in extended-elevation metres (chainage, absolute Z).</summary>
public sealed class SectionSheetStroke
{
    public List<(float X, float Y)> Points { get; init; } = new();

    public bool Closed { get; init; }

    public DesignLayerInkMetadata Metadata { get; init; } = DesignLayerInkMetadata.ForUserStroke(
        SketchStrokeStyleDefaults.DefaultStrokeWidthPx);
}

/// <summary>Symbol or text label on the section sheet, in extended-elevation metres.</summary>
public sealed class SectionSheetSymbol
{
    public float X { get; init; }

    public float Y { get; init; }

    public string SymbolId { get; init; } = "";

    public string? Label { get; init; }

    public float Scale { get; init; } = 1f;

    public bool IsLabel =>
        string.Equals(SymbolId, DesignLayerSurveyConverter.LabelIcon, StringComparison.Ordinal);
}

/// <summary>
/// Section-sheet ink for the Windows extended elevation (developed chainage × station Z).
/// Android <c>sectionSketches</c> is a list of point-pairs; object rows there fail Gson import.
/// Strokes are stored as <c>vectorLines</c> (<see cref="SurveyStationGeometry.WindowsSectionVectorLineType"/>),
/// which is the layer the Android pen already draws. <c>viewMode</c> is the long profile (3):
/// X is horizontal extended distance and Y is elevation relative to the entrance.
/// The Windows sheet still edits slope-tape chainage and converts on save and load.
/// The phone Section tab is radial, so it does not draw this layer.
/// </summary>
public static class SectionSheetInk
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    public static SurveyStationGeometry.PlanVectorPolyline ToWallPolyline(SectionSheetStroke stroke)
    {
        int? argb = stroke.Metadata.StrokeColorArgb is uint packed ? unchecked((int)packed) : null;
        return new SurveyStationGeometry.PlanVectorPolyline(
            "section",
            stroke.Points,
            stroke.Closed,
            PreferSharpPolyline: true,
            argb);
    }

    public static IReadOnlyList<SurveyStationGeometry.PlanMapSymbol> ReadMapSymbols(CaveProjectDocument project)
    {
        var list = new List<SurveyStationGeometry.PlanMapSymbol>();
        foreach (var sym in ReadSymbols(project))
        {
            if (sym.IsLabel && string.IsNullOrWhiteSpace(sym.Label))
                continue;
            list.Add(new SurveyStationGeometry.PlanMapSymbol(
                sym.X,
                sym.Y,
                0f,
                sym.IsLabel ? sym.Label : null,
                sym.Scale > 0 ? sym.Scale : 1f,
                0f,
                sym.SymbolId,
                sym.SymbolId));
        }

        return list;
    }

    public static IReadOnlyList<SectionSheetStroke> ReadStrokes(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var rows = SelectViewMode(EnumerateVectorLines(project).Where(SurveyStationGeometry.IsWindowsSectionVectorLine));
        var alt = SurveyStationGeometry.EntranceAltitudeMetres(project);
        var list = new List<SectionSheetStroke>();
        foreach (var el in rows)
        {
            if (!TryReadPoints(el, out var stored) || stored.Count < 2)
                continue;
            var closed = el.TryGetProperty("closed", out var closedEl) && closedEl.ValueKind == JsonValueKind.True;
            if (closed && stored.Count >= 3 && NearlySame(stored[0], stored[^1]))
                stored.RemoveAt(stored.Count - 1);
            if (stored.Count < 2)
                continue;

            list.Add(new SectionSheetStroke
            {
                Closed = closed,
                Metadata = SketchStrokeStyleMerger.TryParseFromJson(el)
                           ?? DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx),
                Points = stored.Select(p => (
                    SectionSheetChainage.ToSlopeX(project, p.X),
                    p.Y + alt)).ToList(),
            });
        }

        return list;
    }

    public static IReadOnlyList<SectionSheetSymbol> ReadSymbols(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.MapSymbols.ValueKind != JsonValueKind.Array)
            return Array.Empty<SectionSheetSymbol>();

        var rows = SelectViewMode(project.MapSymbols.EnumerateArray().Where(SurveyStationGeometry.IsExtendedElevationCartography));
        var alt = SurveyStationGeometry.EntranceAltitudeMetres(project);
        var list = new List<SectionSheetSymbol>();
        foreach (var el in rows)
        {
            if (!TryReadSymbolXY(el, out var x, out var y))
                continue;
            var symbolId = ReadSymbolId(el);
            if (string.IsNullOrWhiteSpace(symbolId))
                continue;
            var scale = 1f;
            if (el.TryGetProperty("scale", out var scaleEl) && scaleEl.TryGetSingle(out var readScale) && readScale > 0)
                scale = readScale;
            string? label = null;
            if (el.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String)
                label = labelEl.GetString();
            list.Add(new SectionSheetSymbol
            {
                X = SectionSheetChainage.ToSlopeX(project, x),
                Y = y + alt,
                SymbolId = symbolId,
                Label = label,
                Scale = scale,
            });
        }

        return list;
    }

    /// <summary>Writes the design layer onto section vector lines and extended-elevation symbols. Plan ink is left in place.</summary>
    public static bool TryApply(
        CaveProjectDocument project,
        System.Windows.Controls.Canvas? designLayer,
        PlanCanvasSurveyLayout layout)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (layout.Scale <= 0 || double.IsNaN(layout.Scale))
            return false;

        var alt = SurveyStationGeometry.EntranceAltitudeMetres(project);
        var (strokes, stamps) = DesignLayerSurveyConverter.ExtractUserGeometry(designLayer, layout);
        var labels = DesignLayerSurveyConverter.ExtractLabels(designLayer, layout);

        var vectorRows = new List<object>();
        foreach (var stroke in strokes)
        {
            var relative = RelativePoints(project, stroke.Points, alt, stroke.Closed);
            vectorRows.Add(BuildVectorLine(relative, stroke, SurveyStationGeometry.AndroidViewModeLongProfile));
        }

        project.VectorLines = MergeVectorLines(project.VectorLines, vectorRows);

        var symbolRows = new List<object>();
        foreach (var stamp in stamps)
        {
            symbolRows.Add(BuildSymbol(
                SectionSheetChainage.ToExtendedX(project, stamp.SurveyX),
                stamp.SurveyY - alt,
                SketchEditorSymbolKindExporter.ToSymbolId(stamp.Kind),
                label: null,
                stamp.Scale,
                SurveyStationGeometry.AndroidViewModeLongProfile));
        }

        foreach (var label in labels)
        {
            symbolRows.Add(BuildSymbol(
                SectionSheetChainage.ToExtendedX(project, label.X),
                label.Y - alt,
                DesignLayerSurveyConverter.LabelIcon,
                label.Text,
                1f,
                SurveyStationGeometry.AndroidViewModeLongProfile));
        }

        project.MapSymbols = ReplaceExtendedElevationSymbols(project.MapSymbols, symbolRows);
        if (string.IsNullOrWhiteSpace(project.SurveyArchiveSchemaVersion))
            project.SurveyArchiveSchemaVersion = "2";
        DesignLayerMapObjectsSerializer.TouchWindowsEditMetadata(project);
        return true;
    }

    /// <summary>Plan save replaces <c>mapSymbols</c>. Keep section-sheet stamps that live in the same array.</summary>
    public static JsonElement PreserveExtendedElevationSymbols(JsonElement existing, JsonElement planSymbols)
    {
        var list = new List<JsonElement>();
        if (planSymbols.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in planSymbols.EnumerateArray())
                list.Add(el.Clone());
        }

        if (existing.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in existing.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object && SurveyStationGeometry.IsExtendedElevationCartography(el))
                    list.Add(el.Clone());
            }
        }

        return JsonSerializer.SerializeToElement(list, CompactJson);
    }

    private static Dictionary<string, object?> BuildVectorLine(
        IReadOnlyList<(float X, float Y)> relativePoints,
        SketchStrokeModel stroke,
        int viewMode)
    {
        var row = new Dictionary<string, object?>
        {
            ["type"] = SurveyStationGeometry.WindowsSectionVectorLineType,
            ["viewMode"] = viewMode,
            ["closed"] = stroke.Closed,
            ["points"] = relativePoints.Select(p => new Dictionary<string, object?>
            {
                ["first"] = p.X,
                ["second"] = p.Y,
            }).ToArray(),
            ["sourceClient"] = "CaveAiProForWindows",
            ["coordinateFrame"] = SurveyStationGeometry.ExtendedElevationCoordinateFrame,
        };
        SketchStrokeStyleMerger.MergeInto(row, stroke.Metadata);
        return row;
    }

    private static Dictionary<string, object?> BuildSymbol(
        float x,
        float y,
        string symbolId,
        string? label,
        float scale,
        int viewMode)
    {
        var row = new Dictionary<string, object?>
        {
            ["icon"] = symbolId,
            ["symbolId"] = symbolId,
            ["x"] = x,
            ["y"] = y,
            ["surveyX"] = x,
            ["surveyY"] = y,
            ["viewMode"] = viewMode,
            ["scale"] = scale > 0 ? scale : 1f,
            ["sourceClient"] = "CaveAiProForWindows",
            ["coordinateFrame"] = SurveyStationGeometry.ExtendedElevationCoordinateFrame,
        };
        if (!string.IsNullOrWhiteSpace(label))
            row["label"] = label;
        return row;
    }

    private static List<(float X, float Y)> RelativePoints(
        CaveProjectDocument project,
        IReadOnlyList<(float X, float Y)> absolute,
        float entranceAlt,
        bool closed)
    {
        var pts = absolute.Select(p => (
            SectionSheetChainage.ToExtendedX(project, p.X),
            p.Y - entranceAlt)).ToList();
        if (closed && pts.Count >= 3 && !NearlySame(pts[0], pts[^1]))
            pts.Add(pts[0]);
        return pts;
    }

    private static JsonElement MergeVectorLines(JsonElement? existing, List<object> windowsRows)
    {
        var merged = new List<JsonElement>();
        if (existing is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object && SurveyStationGeometry.IsWindowsSectionVectorLine(el))
                    continue;
                merged.Add(el.Clone());
            }
        }

        if (windowsRows.Count > 0)
        {
            var written = JsonSerializer.SerializeToElement(windowsRows, CompactJson);
            foreach (var el in written.EnumerateArray())
                merged.Add(el.Clone());
        }

        return JsonSerializer.SerializeToElement(merged, CompactJson);
    }

    private static JsonElement ReplaceExtendedElevationSymbols(JsonElement existing, List<object> incoming)
    {
        var list = new List<JsonElement>();
        if (existing.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in existing.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object && SurveyStationGeometry.IsExtendedElevationCartography(el))
                    continue;
                list.Add(el.Clone());
            }
        }

        if (incoming.Count > 0)
        {
            var written = JsonSerializer.SerializeToElement(incoming, CompactJson);
            foreach (var el in written.EnumerateArray())
                list.Add(el.Clone());
        }

        return JsonSerializer.SerializeToElement(list, CompactJson);
    }

    private static IEnumerable<JsonElement> EnumerateVectorLines(CaveProjectDocument project)
    {
        if (project.VectorLines is not { ValueKind: JsonValueKind.Array } arr)
            yield break;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.Object)
                yield return el;
        }
    }

    private static List<JsonElement> SelectViewMode(IEnumerable<JsonElement> rows)
    {
        var list = rows.ToList();
        var profile = list.Where(el => ReadViewMode(el) == SurveyStationGeometry.AndroidViewModeLongProfile).ToList();
        if (profile.Count > 0)
            return profile;
        return list.Where(el => ReadViewMode(el) == SurveyStationGeometry.AndroidViewModeSection).ToList();
    }

    private static int ReadViewMode(JsonElement el)
    {
        if (el.TryGetProperty("viewMode", out var vm) && vm.TryGetInt32(out var mode))
            return mode;
        return SurveyStationGeometry.AndroidViewModeSection;
    }

    private static bool TryReadPoints(JsonElement el, out List<(float X, float Y)> pts)
    {
        pts = new List<(float X, float Y)>();
        if (!el.TryGetProperty("points", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
            {
                pts.Add((ReadFloat(p[0]), ReadFloat(p[1])));
                continue;
            }

            if (p.ValueKind == JsonValueKind.Object
                && p.TryGetProperty("first", out var first)
                && p.TryGetProperty("second", out var second))
                pts.Add((ReadFloat(first), ReadFloat(second)));
        }

        return pts.Count >= 2;
    }

    private static bool TryReadSymbolXY(JsonElement el, out float x, out float y)
    {
        x = y = 0;
        if (TryReadFloatProp(el, "x", out x) && TryReadFloatProp(el, "y", out y))
            return true;
        return TryReadFloatProp(el, "surveyX", out x) && TryReadFloatProp(el, "surveyY", out y);
    }

    private static string ReadSymbolId(JsonElement el)
    {
        foreach (var name in new[] { "symbolId", "icon" })
        {
            if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return "";
    }

    private static bool TryReadFloatProp(JsonElement el, string name, out float value)
    {
        value = 0;
        return el.TryGetProperty(name, out var prop) && TryReadFloat(prop, out value);
    }

    private static float ReadFloat(JsonElement el) =>
        TryReadFloat(el, out var value) ? value : 0f;

    private static bool TryReadFloat(JsonElement el, out float value)
    {
        value = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out value))
            return true;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d))
        {
            value = (float)d;
            return true;
        }

        return el.ValueKind == JsonValueKind.String
               && float.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static bool NearlySame((float X, float Y) a, (float X, float Y) b) =>
        Math.Abs(a.X - b.X) <= 1e-3f && Math.Abs(a.Y - b.Y) <= 1e-3f;
}
