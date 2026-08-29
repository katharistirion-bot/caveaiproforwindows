using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>Rebuilds Sketch Editor design-layer ink from persisted <c>mapObjects</c> / Windows <c>sketches</c> JSON.</summary>
public static class DesignLayerMapObjectsHydrator
{
    public static int TryHydrate(Canvas designLayer, PlanCanvasSurveyLayout layout, CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(designLayer);
        ArgumentNullException.ThrowIfNull(project);
        if (layout.Scale <= 0 || double.IsNaN(layout.Scale))
            return 0;

        var entries = CollectEntries(project);
        if (entries.Count == 0)
            return 0;

        designLayer.Children.Clear();

        // When a named cartography map is open, prefer hydrating from the named-map live layers
        // (project.Sketches / project.MapSymbols). The base project.MapObjects can be stale and causes
        // the "split brain" between named cartography and sketch editor ink.
        var activeMapId = project.ActiveCartographyMapId?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(activeMapId))
        {
            var addedNamed = 0;

            // 1) Strokes
            var sketchEntries = new List<JsonElement>();
            AppendWindowsSketchEntries(sketchEntries, project.Sketches);
            foreach (var el in sketchEntries)
            {
                if (TryHydrateStroke(designLayer, layout, el))
                    addedNamed++;
            }

            // 2) Symbols (Android mapSymbols schema: icon + x/y + viewMode)
            addedNamed += HydrateSymbolsFromMapSymbols(designLayer, layout, project.MapSymbols);
            return addedNamed;
        }

        // Fallback (no named map open): hydrate Windows-authored strokes/symbols only.
        // Android / procedural ink stays on SurveyCanvas — avoids double-paint.
        var added = 0;
        foreach (var el in entries)
        {
            if (!IsWindowsAuthored(el))
                continue;
            if (TryHydrateStroke(designLayer, layout, el))
            {
                added++;
                continue;
            }

            if (TryHydrateSymbol(designLayer, layout, el))
                added++;
        }

        return added;
    }

    private static bool IsWindowsAuthored(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (!el.TryGetProperty("sourceClient", out var sc) || sc.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(sc.GetString(), "CaveAiProForWindows", StringComparison.Ordinal);
    }

    private static List<JsonElement> CollectEntries(CaveProjectDocument project)
    {
        var list = new List<JsonElement>();
        AppendArrayEntries(list, project.MapObjects);
        if (list.Count == 0)
            AppendWindowsSketchEntries(list, project.Sketches);
        return list;
    }

    private static int HydrateSymbolsFromMapSymbols(Canvas canvas, PlanCanvasSurveyLayout layout, JsonElement mapSymbols)
    {
        if (mapSymbols.ValueKind != JsonValueKind.Array)
            return 0;

        var added = 0;
        foreach (var el in mapSymbols.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            if (!el.TryGetProperty("icon", out var iconEl) || iconEl.ValueKind != JsonValueKind.String)
                continue;
            if (!TryReadFloat(el, "x", out var x) || !TryReadFloat(el, "y", out var y))
                continue;

            var symbolId = iconEl.GetString();
            if (string.IsNullOrWhiteSpace(symbolId))
                continue;

            var kind = SketchEditorSymbolKindExporter.FromSymbolId(symbolId);
            var meta = DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx);
            var stamp = new SketchSymbolStampModel { SurveyX = x, SurveyY = y, Kind = kind };
            canvas.Children.Add(DesignLayerProceduralApplicator.CreateSymbolStamp(stamp, layout, meta));
            added++;
        }

        return added;
    }

    private static bool TryReadFloat(JsonElement obj, string propertyName, out float value)
    {
        value = 0;
        if (!obj.TryGetProperty(propertyName, out var el))
            return false;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out value))
            return true;

        if (el.ValueKind == JsonValueKind.String &&
            float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static void AppendWindowsSketchEntries(List<JsonElement> list, JsonElement sketches)
    {
        if (sketches.ValueKind != JsonValueKind.Array)
            return;
        foreach (var el in sketches.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            if (el.TryGetProperty("sourceClient", out var sc)
                && sc.ValueKind == JsonValueKind.String
                && string.Equals(sc.GetString(), "CaveAiProForWindows", StringComparison.Ordinal))
                list.Add(el);
        }
    }

    private static void AppendArrayEntries(List<JsonElement> list, JsonElement arrayEl)
    {
        if (arrayEl.ValueKind != JsonValueKind.Array)
            return;
        foreach (var el in arrayEl.EnumerateArray())
            list.Add(el);
    }

    private static bool TryHydrateStroke(Canvas canvas, PlanCanvasSurveyLayout layout, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;
        if (IsSymbolEntry(el))
            return false;

        var points = ReadPoints(el);
        if (points.Count < 2)
            return false;

        var meta = SketchStrokeStyleMerger.TryParseFromJson(el)
            ?? DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx);
        var closed = el.TryGetProperty("closed", out var c) && c.ValueKind == JsonValueKind.True;

        var stroke = new SketchStrokeModel
        {
            Closed = closed,
            Source = meta.Source,
            Points = points,
        };
        var poly = DesignLayerProceduralApplicator.CreatePolyline(stroke, layout, meta);
        if (closed && poly.Points.Count >= 3)
            poly.StrokeLineJoin = PenLineJoin.Round;
        canvas.Children.Add(poly);
        return true;
    }

    private static bool TryHydrateSymbol(Canvas canvas, PlanCanvasSurveyLayout layout, JsonElement el)
    {
        if (!IsSymbolEntry(el))
            return false;

        if (!TryReadSurveyXY(el, out var sx, out var sy))
            return false;

        var symbolId = el.TryGetProperty("symbolId", out var sid) && sid.ValueKind == JsonValueKind.String
            ? sid.GetString()
            : null;
        var kind = SketchEditorSymbolKindExporter.FromSymbolId(symbolId);
        var meta = SketchStrokeStyleMerger.TryParseFromJson(el)
            ?? DesignLayerInkMetadata.ForUserStroke(SketchStrokeStyleDefaults.DefaultStrokeWidthPx);

        var stamp = new SketchSymbolStampModel { SurveyX = sx, SurveyY = sy, Kind = kind };
        canvas.Children.Add(DesignLayerProceduralApplicator.CreateSymbolStamp(stamp, layout, meta));
        return true;
    }

    private static bool IsSymbolEntry(JsonElement el) =>
        el.TryGetProperty("symbolId", out _)
        || (el.TryGetProperty("kind", out var k)
            && k.ValueKind == JsonValueKind.String
            && !string.Equals(k.GetString(), "stroke", StringComparison.OrdinalIgnoreCase)
            && !el.TryGetProperty("points", out _));

    private static List<(float X, float Y)> ReadPoints(JsonElement el)
    {
        var list = new List<(float, float)>();
        if (!el.TryGetProperty("points", out var pts) || pts.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var pt in pts.EnumerateArray())
        {
            if (pt.ValueKind == JsonValueKind.Array)
            {
                var arr = pt.EnumerateArray().ToArray();
                if (arr.Length >= 2
                    && TryReadFloat(arr[0], out var x)
                    && TryReadFloat(arr[1], out var y))
                    list.Add((x, y));
                continue;
            }

            if (pt.ValueKind == JsonValueKind.Object
                && pt.TryGetProperty("x", out var xEl)
                && pt.TryGetProperty("y", out var yEl)
                && TryReadFloat(xEl, out var ox)
                && TryReadFloat(yEl, out var oy))
                list.Add((ox, oy));
        }

        return list;
    }

    private static bool TryReadSurveyXY(JsonElement el, out float x, out float y)
    {
        x = y = 0;
        if (el.TryGetProperty("surveyX", out var sx) && el.TryGetProperty("surveyY", out var sy)
            && TryReadFloat(sx, out x) && TryReadFloat(sy, out y))
            return true;
        return false;
    }

    private static bool TryReadFloat(JsonElement el, out float value)
    {
        value = 0;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetSingle(out value) => true,
            JsonValueKind.Number when el.TryGetDouble(out var d) => TryFinite((float)d, out value),
            JsonValueKind.String when float.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) => true,
            _ => false,
        };
    }

    private static bool TryFinite(float v, out float value)
    {
        if (float.IsNaN(v) || float.IsInfinity(v))
        {
            value = 0;
            return false;
        }

        value = v;
        return true;
    }
}
