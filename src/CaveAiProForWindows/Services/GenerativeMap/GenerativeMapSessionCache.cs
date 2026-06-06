using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>In-memory generative render for the active project (not persisted to ui-settings.json).</summary>
public static class GenerativeMapSessionCache
{
    private static readonly object Gate = new();

    private static readonly Dictionary<string, GenerativeMapSessionEntry> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    public static event EventHandler<GenerativeMapSessionChangedEventArgs>? SessionChanged;

    public static string KeyFor(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var name = (project.Name ?? "").Trim();
        var date = (project.Date ?? "").Trim();
        var shots = project.Shots?.Count ?? 0;
        return $"{name}|{date}|{shots}";
    }

    public static GenerativeMapSessionEntry? TryGet(CaveProjectDocument? project)
    {
        if (project == null)
            return null;
        var key = KeyFor(project);
        lock (Gate)
            return Entries.TryGetValue(key, out var entry) ? entry : null;
    }

    public static void Set(CaveProjectDocument project, byte[] pngBytes, byte[]? structureMaskPng = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var bitmap = LoadFrozenBitmap(pngBytes);
        var entry = new GenerativeMapSessionEntry
        {
            PngBytes = pngBytes,
            Bitmap = bitmap,
            StructureMaskPng = structureMaskPng is { Length: > 0 } ? structureMaskPng : null,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        lock (Gate)
            Entries[KeyFor(project)] = entry;
        RaiseChanged(project, GenerativeMapSessionChangeKind.Updated);
    }

    public static void Clear(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (Gate)
            Entries.Remove(KeyFor(project));
        RaiseChanged(project, GenerativeMapSessionChangeKind.Cleared);
    }

    private static void RaiseChanged(CaveProjectDocument project, GenerativeMapSessionChangeKind kind) =>
        SessionChanged?.Invoke(null, new GenerativeMapSessionChangedEventArgs(project, kind));

    private static BitmapSource LoadFrozenBitmap(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}

public enum GenerativeMapSessionChangeKind
{
    Updated,
    Cleared,
}

public sealed class GenerativeMapSessionChangedEventArgs : EventArgs
{
    public GenerativeMapSessionChangedEventArgs(CaveProjectDocument project, GenerativeMapSessionChangeKind kind)
    {
        Project = project;
        Kind = kind;
    }

    public CaveProjectDocument Project { get; }

    public GenerativeMapSessionChangeKind Kind { get; }
}

public sealed class GenerativeMapSessionEntry
{
    public required byte[] PngBytes { get; init; }

    public required BitmapSource Bitmap { get; init; }

    public byte[]? StructureMaskPng { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}
