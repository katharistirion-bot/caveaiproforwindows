using System.Globalization;
using System.Text.Json;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>C# equivalent of Android <c>mergeSketchStrokeStyle</c> for Windows export.</summary>
public static class SketchStrokeStyleMerger
{
    public static void MergeInto(Dictionary<string, object?> stroke, DesignLayerInkMetadata? meta)
    {
        if (meta == null)
        {
            stroke["strokeWidthPx"] = SketchStrokeStyleDefaults.DefaultStrokeWidthPx;
            stroke["strokeColorArgb"] = (long)SketchStrokeStyleDefaults.DefaultStrokeColorArgb;
            stroke["brushProfile"] = SketchStrokeStyleDefaults.DefaultBrushProfile;
            stroke["layerIndex"] = SketchStrokeStyleDefaults.UserLayerIndex;
            stroke["layerZOrder"] = SketchStrokeStyleDefaults.UserLayerZOrder;
            return;
        }

        if (meta.StrokeWidthPx is > 0)
            stroke["strokeWidthPx"] = meta.StrokeWidthPx.Value;
        if (meta.StrokeColorArgb is uint argb)
        {
            stroke["strokeColorArgb"] = (long)argb;
            stroke["strokeColor"] = ArgbToHex(argb);
        }

        if (!string.IsNullOrWhiteSpace(meta.BrushProfile))
            stroke["brushProfile"] = meta.BrushProfile;
        if (meta.LayerIndex is int li)
            stroke["layerIndex"] = li;
        if (meta.LayerZOrder is int lz)
            stroke["layerZOrder"] = lz;
        if (!string.IsNullOrWhiteSpace(meta.LayerName))
            stroke["layerName"] = meta.LayerName;
    }

    public static DesignLayerInkMetadata? TryParseFromJson(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        var source = ReadString(el, "sourceClient") ?? SketchStrokeStyleDefaults.DesignLayerSource;
        if (string.Equals(source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase))
            source = SketchStrokeStyleDefaults.ProceduralSource;

        return new DesignLayerInkMetadata
        {
            Source = source,
            StrokeWidthPx = ReadDouble(el, "strokeWidthPx"),
            StrokeColorArgb = ReadArgb(el),
            BrushProfile = ReadString(el, "brushProfile"),
            LayerIndex = ReadInt(el, "layerIndex"),
            LayerZOrder = ReadInt(el, "layerZOrder"),
            LayerName = ReadString(el, "layerName"),
        };
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static double? ReadDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds) => ds,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var is_) => is_,
            _ => null,
        };
    }

    private static uint? ReadArgb(JsonElement el)
    {
        if (el.TryGetProperty("strokeColorArgb", out var argbEl))
        {
            long? v = argbEl.ValueKind switch
            {
                JsonValueKind.Number when argbEl.TryGetInt64(out var l) => l,
                JsonValueKind.String when long.TryParse(argbEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ls) => ls,
                _ => null,
            };
            if (v is >= 0 and <= uint.MaxValue)
                return (uint)v;
        }

        var hex = ReadString(el, "strokeColor");
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        return TryParseHexColor(hex);
    }

    private static uint? TryParseHexColor(string hex)
    {
        var s = hex.Trim().TrimStart('#');
        if (s.Length is 6 or 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            if (s.Length == 6)
                return 0xFF000000u | v;
            return v;
        }

        return null;
    }

    private static string ArgbToHex(uint argb) =>
        $"#{(argb & 0xFFFFFFFF):X8}";
}
