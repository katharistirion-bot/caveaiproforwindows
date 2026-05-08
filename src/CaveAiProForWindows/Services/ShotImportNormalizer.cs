using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// After JSON deserialization, maps alternate Gson/Android LRUD and station keys onto
/// <see cref="ShotRecord.L"/>/<c>R</c>/<c>U</c>/<c>D</c> so plan X-ray geometry can build.
/// </summary>
public static class ShotImportNormalizer
{
    private const float Eps = 1e-4f;

    public static void NormalizeProjectShots(CaveProjectDocument project)
    {
        foreach (var s in project.Shots)
            NormalizeShot(s);
    }

    internal static void NormalizeShot(ShotRecord s)
    {
        if (!string.IsNullOrWhiteSpace(s.FromAlternate) && string.IsNullOrWhiteSpace(s.FromStation))
            s.FromStation = s.FromAlternate.Trim();
        if (!string.IsNullOrWhiteSpace(s.ToAlternate) && string.IsNullOrWhiteSpace(s.ToStation))
            s.ToStation = s.ToAlternate.Trim();

        s.FromStation = (s.FromStation ?? "").Trim();
        s.ToStation = (s.ToStation ?? "").Trim();

        if (LrudAllSmall(s))
            ApplyAliasDimensions(s);

        if (LrudAllSmall(s) && s.ExtensionData != null)
        {
            foreach (var wrapKey in new[]
                     {
                         "lrud", "LRUD", "passage", "passageDimensions", "dimensions", "crossSection",
                         "passageSize", "tubeDims", "wallDims", "cross_section", "stationLrud", "lrudMeters",
                     })
            {
                if (!s.ExtensionData.TryGetValue(wrapKey, out var wrap))
                    continue;
                if (wrap.ValueKind == JsonValueKind.Object)
                    PullLrudObject(wrap, s);
                else if (wrap.ValueKind == JsonValueKind.Array && wrap.GetArrayLength() >= 4)
                    PullLrudArray(wrap, s);
                if (!LrudAllSmall(s))
                    break;
            }

            // Gson may nest LRUD under an unlisted object key — scan top-level extension objects.
            if (LrudAllSmall(s))
            {
                foreach (var kv in s.ExtensionData)
                {
                    if (kv.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    PullLrudObject(kv.Value, s);
                    if (!LrudAllSmall(s))
                        break;
                }
            }
        }

        TryPullRadialsFromExtension(s);
        TryPullAzimuthClinoFromExtension(s);
    }

    /// <summary>Some Gson exports nest azimuth/clino under a single object instead of top-level fields.</summary>
    private static void TryPullAzimuthClinoFromExtension(ShotRecord s)
    {
        if (s.ExtensionData == null)
            return;
        foreach (var wrapKey in new[] { "angles", "orientation", "direction", "compass", "bearing" })
        {
            if (!s.ExtensionData.TryGetValue(wrapKey, out var wrap) || wrap.ValueKind != JsonValueKind.Object)
                continue;
            var wrote = false;
            if (TryReadFirstFloatAnyMagnitude(wrap, out var az, "azimuth", "az", "bearing", "heading", "deg"))
            {
                s.Azimuth = az;
                wrote = true;
            }

            if (TryReadFirstFloatAnyMagnitude(wrap, out var cl, "clino", "inclination", "dip", "slope"))
            {
                s.Clino = cl;
                wrote = true;
            }

            if (wrote)
                return;
        }
    }

    /// <summary>Like <see cref="TryFirstFloat"/> but allows zero (needed for compass angles).</summary>
    private static bool TryReadFirstFloatAnyMagnitude(JsonElement obj, out float value, params string[] names)
    {
        foreach (var n in names)
        {
            if (!obj.TryGetProperty(n, out var el))
                continue;
            if (TryGetFloat(el, out var v))
            {
                value = v;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool LrudAllSmall(ShotRecord s) =>
        Math.Abs(s.L) < Eps && Math.Abs(s.R) < Eps && Math.Abs(s.U) < Eps && Math.Abs(s.D) < Eps;

    private static void ApplyAliasDimensions(ShotRecord s)
    {
        if (Math.Abs(s.L) < Eps && s.LeftAlias is float lv && Math.Abs(lv) >= Eps)
            s.L = lv;
        if (Math.Abs(s.R) < Eps && s.RightAlias is float rv && Math.Abs(rv) >= Eps)
            s.R = rv;
        if (Math.Abs(s.U) < Eps && s.UpAlias is float uv && Math.Abs(uv) >= Eps)
            s.U = uv;
        if (Math.Abs(s.D) < Eps && s.DownAlias is float dv && Math.Abs(dv) >= Eps)
            s.D = dv;
    }

    private static void PullLrudObject(JsonElement obj, ShotRecord s)
    {
        if (Math.Abs(s.L) < Eps && TryFirstFloat(obj, out var l, "l", "L", "left", "Left", "leftM", "leftWall", "passageLeft", "wallL"))
            s.L = l;
        if (Math.Abs(s.R) < Eps && TryFirstFloat(obj, out var r, "r", "R", "right", "Right", "rightM", "rightWall", "passageRight", "wallR"))
            s.R = r;
        if (Math.Abs(s.U) < Eps && TryFirstFloat(obj, out var u, "u", "U", "up", "Up", "upM", "ceiling", "Ceiling", "passageUp", "wallU"))
            s.U = u;
        if (Math.Abs(s.D) < Eps && TryFirstFloat(obj, out var d, "d", "D", "down", "Down", "downM", "floor", "Floor", "passageDown", "wallD"))
            s.D = d;
    }

    /// <summary>12 plan radials sometimes land in extension data (alternate Gson keys) instead of <see cref="ShotRecord.Radials"/>.</summary>
    private static void TryPullRadialsFromExtension(ShotRecord s)
    {
        if (s.Radials.Count >= 12)
            return;
        if (s.ExtensionData == null)
            return;

        static bool KeyLooksRadial(string name) =>
            name.Contains("radial", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("splayWatch", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("planSample", StringComparison.OrdinalIgnoreCase);

        foreach (var kv in s.ExtensionData)
        {
            if (kv.Value.ValueKind != JsonValueKind.Array || kv.Value.GetArrayLength() < 12)
                continue;
            if (!KeyLooksRadial(kv.Key))
                continue;
            if (!TryReadFloatList(kv.Value, 12, out var list))
                continue;
            s.Radials = list;
            return;
        }
    }

    private static bool TryReadFloatList(JsonElement arr, int minLen, out List<float> list)
    {
        list = new List<float>(minLen);
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < minLen)
            return false;
        var n = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (!TryGetFloat(el, out var v))
                return false;
            list.Add(v);
            n++;
            if (n >= minLen)
                break;
        }

        return list.Count >= minLen;
    }

    private static bool TryFirstFloat(JsonElement obj, out float value, params string[] names)
    {
        foreach (var n in names)
        {
            if (!obj.TryGetProperty(n, out var el))
                continue;
            if (TryGetFloat(el, out var v) && Math.Abs(v) >= Eps)
            {
                value = v;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static void PullLrudArray(JsonElement arr, ShotRecord s)
    {
        var a = arr.EnumerateArray().ToArray();
        if (a.Length < 4)
            return;
        if (TryGetFloat(a[0], out var l) && Math.Abs(s.L) < Eps)
            s.L = l;
        if (TryGetFloat(a[1], out var r) && Math.Abs(s.R) < Eps)
            s.R = r;
        if (TryGetFloat(a[2], out var u) && Math.Abs(s.U) < Eps)
            s.U = u;
        if (TryGetFloat(a[3], out var d) && Math.Abs(s.D) < Eps)
            s.D = d;
    }

    private static bool TryGetFloat(JsonElement el, out float v)
    {
        v = 0;
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                if (el.TryGetSingle(out v))
                    return true;
                if (el.TryGetDouble(out var d))
                {
                    v = (float)d;
                    return true;
                }

                return false;
            case JsonValueKind.String:
                return float.TryParse(el.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v);
            default:
                return false;
        }
    }
}
