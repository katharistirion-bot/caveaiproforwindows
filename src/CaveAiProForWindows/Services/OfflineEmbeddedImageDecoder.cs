using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace CaveAiProForWindows.Services;

/// <summary>Offline-only helpers: embed <c>data:image/…;base64,…</c> or raw base64 from Android JSON.</summary>
public static class OfflineEmbeddedImageDecoder
{
    public static BitmapSource? TryDecodeDataUriOrBase64(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var s = raw.Trim();
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = s.IndexOf(',');
            if (comma < 0)
                return null;
            var meta = s[..comma];
            var payload = s[(comma + 1)..];
            if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
                return TryDecodeBase64ToBitmap(payload);
            return null;
        }

        if (LooksLikeBase64Image(s))
            return TryDecodeBase64ToBitmap(s);
        return null;
    }

    private static bool LooksLikeBase64Image(string s)
    {
        if (s.Length < 80)
            return false;
        var nonWhitespace = 0;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
                continue;
            nonWhitespace++;
            if (c is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '+' and not '/' and not '=')
                return false;
        }

        return nonWhitespace >= 80;
    }

    private static BitmapSource? TryDecodeBase64ToBitmap(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64.Replace("\n", "").Replace("\r", "").Replace(" ", ""));
            if (bytes.Length < 32)
                return null;
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deep scan for first decodable embed under optional per-property key filter on string leaves.</summary>
    public static BitmapSource? TryFindEmbeddedBitmapInJson(JsonElement root, Func<string, bool>? keyHint = null)
    {
        return WalkFirst(root, keyHint);
    }

    /// <summary>All decodable image embeds reachable from JSON (no disk / network).</summary>
    public static IEnumerable<BitmapSource> EnumerateEmbeddedImages(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.String:
            {
                if (TryDecodeDataUriOrBase64(root.GetString()) is { } b)
                    yield return b;
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in root.EnumerateArray())
                foreach (var b in EnumerateEmbeddedImages(item))
                    yield return b;
                break;
            case JsonValueKind.Object:
                foreach (var p in root.EnumerateObject())
                foreach (var b in EnumerateEmbeddedImages(p.Value))
                    yield return b;
                break;
        }
    }

    public static IEnumerable<BitmapSource> EnumerateEmbeddedImagesFromExtension(
        Dictionary<string, JsonElement>? ext,
        Func<string, bool>? rootKeyHint = null)
    {
        if (ext == null)
            yield break;
        foreach (var kv in ext)
        {
            if (rootKeyHint != null && !rootKeyHint(kv.Key))
                continue;
            foreach (var b in EnumerateEmbeddedImages(kv.Value))
                yield return b;
        }
    }

    private static BitmapSource? WalkFirst(JsonElement el, Func<string, bool>? keyHint, string? key = null)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
            {
                var s = el.GetString();
                if (string.IsNullOrWhiteSpace(s))
                    return null;
                if (keyHint != null && key != null && !keyHint(key))
                    return null;
                return TryDecodeDataUriOrBase64(s);
            }
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                {
                    var r = WalkFirst(item, keyHint, key);
                    if (r != null)
                        return r;
                }

                return null;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    var r = WalkFirst(p.Value, keyHint, p.Name);
                    if (r != null)
                        return r;
                }

                return null;
            default:
                return null;
        }
    }
}
