using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Puts a north-up aerial raster on the project the same way the phone stores Aerial DEM.
/// The next Android backup ZIP carries the file under export_assets/windows.
/// </summary>
public static class SurfaceDemImport
{
    private static readonly Dictionary<string, (string Path, byte[] Bytes)> Staged =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Path, byte[] Bytes)> TakeStaged(CaveProjectDocument project)
    {
        var key = StageKey(project);
        if (!Staged.TryGetValue(key, out var pair))
            return [];
        Staged.Remove(key);
        return [pair];
    }

    public static string Apply(
        CaveProjectDocument project,
        string? zipPath,
        string imageFile,
        double southWestLat,
        double southWestLon,
        double northEastLat,
        double northEastLon)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(imageFile) || !File.Exists(imageFile))
            throw new FileNotFoundException("Choose a raster file.", imageFile);
        if (northEastLat <= southWestLat || northEastLon <= southWestLon)
            throw new InvalidOperationException("The north-east corner must be north and east of the south-west corner.");
        if (Math.Abs(southWestLat) > 90 || Math.Abs(northEastLat) > 90 ||
            Math.Abs(southWestLon) > 180 || Math.Abs(northEastLon) > 180)
            throw new InvalidOperationException("Latitude must be within ±90 and longitude within ±180.");

        var ext = Path.GetExtension(imageFile);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".tif";
        var cave = Sanitize(project.Name);
        var entry = $"export_assets/windows/{cave}/aerial_dem{ext.ToLowerInvariant()}";
        var bytes = File.ReadAllBytes(imageFile);
        if (!string.IsNullOrWhiteSpace(zipPath) && File.Exists(zipPath))
            WriteZipEntry(zipPath, entry, bytes);
        else
            Staged[StageKey(project)] = (entry, bytes);

        var json = JsonSerializer.Serialize(new
        {
            imageUri = entry,
            southWestLat,
            southWestLon,
            northEastLat,
            northEastLon,
            opacity = 0.55,
        });
        using var doc = JsonDocument.Parse(json);
        project.SurfaceLidarRaster = doc.RootElement.Clone();
        return entry;
    }

    private static string StageKey(CaveProjectDocument project) =>
        $"{project.Name}|{project.ProjectId}";

    private static void WriteZipEntry(string zipPath, string entryName, byte[] bytes)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var existing = zip.GetEntry(entryName);
        existing?.Delete();
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string Sanitize(string? name)
    {
        var text = string.IsNullOrWhiteSpace(name) ? "cave" : name.Trim();
        var chars = text.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray();
        var clean = new string(chars);
        return clean.Length == 0 ? "cave" : clean;
    }

    public static bool TryParseCorner(string? text, out double value) =>
        double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
