using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Reads <c>map_inventory.json</c> from CaveAI Pro backup ZIPs (written by Android <c>ExportZipMapInventory</c>).</summary>
public static class ZipMapInventoryReader
{
    public static IReadOnlyList<MapInventoryRow> TryRead(string zipPath)
    {
        var zipFileName = Path.GetFileName(zipPath);
        var list = new List<MapInventoryRow>();
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = FindMapInventoryZipEntry(zip);
            if (entry == null)
                return list;

            using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            var root = doc.RootElement;
            if (!root.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var proj in projects.EnumerateArray())
            {
                if (proj.ValueKind != JsonValueKind.Object)
                    continue;
                var name = JsonString(proj, "projectName");
                var date = JsonString(proj, "projectDate");
                if (!proj.TryGetProperty("maps", out var maps) || maps.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var m in maps.EnumerateArray())
                {
                    if (m.ValueKind != JsonValueKind.Object)
                        continue;
                    var slot = JsonString(m, "slot");
                    var path = JsonString(m, "pathInBackupOrUrl");
                    var kind = JsonString(m, "storageKind");
                    if (string.IsNullOrEmpty(path))
                        continue;
                    list.Add(new MapInventoryRow(zipFileName, name, date, slot, path, kind));
                }
            }
        }
        catch
        {
            /* ignore malformed optional file */
        }

        list.Sort(static (a, b) =>
        {
            var c = string.Compare(a.ProjectName, b.ProjectName, StringComparison.OrdinalIgnoreCase);
            if (c != 0)
                return c;
            c = string.Compare(
                MapAssetSortKeys.OrdinalBracketKey(a.Slot),
                MapAssetSortKeys.OrdinalBracketKey(b.Slot),
                StringComparison.OrdinalIgnoreCase);
            if (c != 0)
                return c;
            return string.Compare(a.PathInBackupOrUrl, b.PathInBackupOrUrl, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }

    /// <summary>ZIP writers vary: root <c>map_inventory.json</c>, nested paths, or odd casing.</summary>
    public static ZipArchiveEntry? FindMapInventoryZipEntry(ZipArchive zip)
    {
        var exact = zip.GetEntry("map_inventory.json");
        if (exact != null)
            return exact;
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name))
                continue;
            var full = ExplorationDataLoader.NormalizeZipEntryPath(e.FullName);
            if (string.Equals(Path.GetFileName(full), "map_inventory.json", StringComparison.OrdinalIgnoreCase))
                return e;
        }

        return null;
    }

    private static string JsonString(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String)
            return "";
        return el.GetString() ?? "";
    }
}
