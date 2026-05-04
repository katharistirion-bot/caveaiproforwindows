using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Best-effort: find a JSON object in <see cref="CaveProjectDocument.ExtensionData"/> that references the same map path/URI
/// and carries min/max corners in survey metres. Android exports vary; this stays defensive.
/// </summary>
public static class RasterUnderlayBoundsParser
{
    public static PlanRasterWorldExtentMetres? TryParse(CaveProjectDocument? project, MapAssetRow row, string resolvedPath)
    {
        if (project?.ExtensionData == null)
            return null;

        try
        {
            var needle = NormalizeNeedle(row.UriOrPath, resolvedPath);
            if (needle.Length == 0)
                return null;

            foreach (var kv in project.ExtensionData)
            {
                if (TryFindInElement(kv.Value, needle, out var ext))
                    return ext;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RasterUnderlayBounds] parse skip: {ex.Message}");
        }

        return null;
    }

    private static string NormalizeNeedle(string? uriOrPath, string resolvedPath)
    {
        var u = (uriOrPath ?? "").Trim().Replace('\\', '/');
        if (u.Length > 0)
            return u;
        return Path.GetFileName(resolvedPath).Replace('\\', '/');
    }

    private static bool TryFindInElement(JsonElement el, string needleNorm, out PlanRasterWorldExtentMetres? extent)
    {
        extent = null;
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                if (ObjectReferencesNeedle(el, needleNorm) && TryReadExtentFromObject(el, out var ex))
                {
                    extent = ex;
                    return true;
                }

                foreach (var p in el.EnumerateObject())
                {
                    if (TryFindInElement(p.Value, needleNorm, out extent))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    if (TryFindInElement(item, needleNorm, out extent))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    private static bool ObjectReferencesNeedle(JsonElement obj, string needleNorm)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return false;
        var needleFile = Path.GetFileName(needleNorm.Replace('/', Path.DirectorySeparatorChar));
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.String)
                continue;
            var s = (p.Value.GetString() ?? "").Trim().Replace('\\', '/');
            if (s.Length == 0)
                continue;
            if (string.Equals(s, needleNorm, StringComparison.OrdinalIgnoreCase))
                return true;
            if (needleNorm.Length > 1 && (s.EndsWith(needleNorm, StringComparison.OrdinalIgnoreCase) ||
                                          needleNorm.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (needleFile.Length > 0 &&
                string.Equals(Path.GetFileName(s), needleFile, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool TryReadExtentFromObject(JsonElement obj, out PlanRasterWorldExtentMetres ext)
    {
        ext = default!;
        if (!TryReadAxisPair(obj, "minX", "maxX", out var minX, out var maxX) &&
            !TryReadAxisPair(obj, "planMinX", "planMaxX", out minX, out maxX) &&
            !TryReadAxisPair(obj, "xMin", "xMax", out minX, out maxX))
            return false;
        if (!TryReadAxisPair(obj, "minY", "maxY", out var minY, out var maxY) &&
            !TryReadAxisPair(obj, "planMinY", "planMaxY", out minY, out maxY) &&
            !TryReadAxisPair(obj, "yMin", "yMax", out minY, out maxY))
            return false;

        if (!IsFinite(minX) || !IsFinite(maxX) || !IsFinite(minY) || !IsFinite(maxY))
            return false;
        if (maxX <= minX || maxY <= minY)
            return false;

        ext = new PlanRasterWorldExtentMetres
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
        };
        return true;
    }

    private static bool TryReadAxisPair(JsonElement obj, string a, string b, out double lo, out double hi)
    {
        lo = hi = 0;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;
        double? va = null;
        double? vb = null;
        foreach (var p in obj.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Number)
                continue;
            if (p.Name.Equals(a, StringComparison.OrdinalIgnoreCase))
                va = p.Value.GetDouble();
            else if (p.Name.Equals(b, StringComparison.OrdinalIgnoreCase))
                vb = p.Value.GetDouble();
        }

        if (va is not { } x0 || vb is not { } x1 || !IsFinite(x0) || !IsFinite(x1))
            return false;
        lo = Math.Min(x0, x1);
        hi = Math.Max(x0, x1);
        return hi > lo;
    }

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
}
