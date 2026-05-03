using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Picks a cover image URI/path for registry cards — mirrors Android library cartography / image fields when resolvable on desktop.</summary>
public static class RegistryCoverResolver
{
    private static readonly string[] ExtensionStringKeys =
    {
        "imageUri", "coverImageUri", "libraryImageUri", "publicLibraryImageUri",
    };

    private static readonly string[] ExtensionArrayKeys =
    {
        "publicLibraryCartographyUris", "publicLibraryCartographyUri",
    };

    public static string? TryGetCoverUri(CaveProjectDocument p)
    {
        var candidates = new List<string>(32);
        foreach (var key in ExtensionStringKeys)
            AddExtensionString(p.ExtensionData, key, candidates);

        foreach (var key in ExtensionArrayKeys)
            AddExtensionArrayStrings(p.ExtensionData, key, candidates);

        foreach (var shot in p.Shots)
        {
            foreach (var ph in shot.Photos)
                TryAdd(candidates, ph);
        }

        foreach (var s in candidates)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            var t = s.Trim();
            if (IsHttpOrHttps(t) || IsFileScheme(t))
                return t;
            try
            {
                if (Path.IsPathRooted(t) && File.Exists(t))
                    return Path.GetFullPath(t);
            }
            catch
            {
                /* invalid path */
            }
        }

        var dir = SafeJsonSiblingDirectory(p.LoadedFromFile);
        if (dir == null)
            return null;

        foreach (var s in candidates)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            var t = s.Trim();
            if (IsHttpOrHttps(t) || IsFileScheme(t) || Path.IsPathRooted(t))
                continue;
            try
            {
                var rel = t.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                var full = Path.GetFullPath(Path.Combine(dir, rel));
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                /* ignore */
            }
        }

        return null;
    }

    private static string? SafeJsonSiblingDirectory(string? loadedFromFile)
    {
        if (string.IsNullOrWhiteSpace(loadedFromFile))
            return null;
        if (!loadedFromFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            var full = Path.GetFullPath(loadedFromFile);
            if (!File.Exists(full))
                return null;
            return Path.GetDirectoryName(full);
        }
        catch
        {
            return null;
        }
    }

    private static void AddExtensionString(Dictionary<string, JsonElement>? ext, string key, List<string> list)
    {
        if (ext == null || !ext.TryGetValue(key, out var el))
            return;
        TryAdd(list, JsonElementToString(el));
    }

    private static void AddExtensionArrayStrings(Dictionary<string, JsonElement>? ext, string key, List<string> list)
    {
        if (ext == null || !ext.TryGetValue(key, out var el))
            return;
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                TryAdd(list, JsonElementToString(item));
        }
        else
            TryAdd(list, JsonElementToString(el));
    }

    private static string? JsonElementToString(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            _ => null,
        };

    private static void TryAdd(List<string> list, string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return;
        list.Add(s);
    }

    private static bool IsHttpOrHttps(string s) =>
        s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool IsFileScheme(string s) =>
        s.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
}
