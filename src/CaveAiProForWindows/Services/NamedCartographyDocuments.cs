using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Open/save named Android cartography documents stored on <c>data.json</c>.</summary>
public static class NamedCartographyDocuments
{
    public readonly record struct Summary(string Id, string Name, bool IsOpen);
    public readonly record struct SaveResult(string Id, string Name, bool Created);
    public readonly record struct DeleteResult(string? OpenedId, string? OpenedName);

    private static readonly JsonElement EmptyArray = ParseClone("[]");
    private static readonly HashSet<string> LayerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sketches",
        "sectionSketches",
        "mapSymbols",
        "vectorLines",
        "brackets",
        "depthSpanAnnotations",
        "updatedAtEpochMs",
    };

    /// <summary>
    /// Migrates unlabeled live drawings into a first named map so older Android surveys still open
    /// something identifiable in the Windows companion. Also repairs a dangling activeCartographyMapId.
    /// Returns true when the project was mutated (call save if so).
    /// </summary>
    public static bool EnsureNamedDocuments(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var changed = false;

        // If the project has live ink but no named maps, wrap the ink into a first named document.
        var hasMaps = TryGetMaps(project, out var existing) &&
                      existing.ValueKind == JsonValueKind.Array &&
                      existing.GetArrayLength() > 0;
        if (!hasMaps && HasLiveDrawings(project))
        {
            var defaultName = string.IsNullOrWhiteSpace(project.Name) ? "Plan" : project.Name.Trim();
            var id = Guid.NewGuid().ToString("D");
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            project.NamedCartographyMaps = BuildMapsJson(writer =>
                WriteNewMap(writer, id, defaultName, now, project));
            project.ActiveCartographyMapId = id;
            changed = true;
        }

        // If maps exist but activeCartographyMapId is missing or dangling, point to the first map.
        if (TryGetMaps(project, out var maps) && maps.GetArrayLength() > 0)
        {
            var openId = project.ActiveCartographyMapId?.Trim() ?? "";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in maps.EnumerateArray())
            {
                var mid = ReadString(map, "id");
                if (!string.IsNullOrWhiteSpace(mid))
                    ids.Add(mid);
            }

            if (!ids.Contains(openId))
            {
                project.ActiveCartographyMapId = ids.FirstOrDefault() ?? "";
                changed = true;
            }
        }

        return changed;
    }

    private static bool HasLiveDrawings(CaveProjectDocument project)
    {
        static bool HasItems(JsonElement el) =>
            el.ValueKind == JsonValueKind.Array && el.GetArrayLength() > 0;
        return HasItems(project.Sketches) ||
               HasItems(project.SectionSketches) ||
               HasItems(project.MapSymbols) ||
               (project.VectorLines is { } vl && HasItems(vl)) ||
               HasItems(project.Brackets) ||
               HasItems(project.DepthSpanAnnotations);
    }

    public static IReadOnlyList<Summary> List(CaveProjectDocument? project)
    {
        if (project == null || !TryGetMaps(project, out var maps))
            return Array.Empty<Summary>();
        var openId = project.ActiveCartographyMapId?.Trim() ?? "";
        var list = new List<Summary>();
        foreach (var map in maps.EnumerateArray())
        {
            var id = ReadString(map, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var name = ReadString(map, "name");
            if (string.IsNullOrWhiteSpace(name))
                name = "Plan";
            list.Add(new Summary(id, name, string.Equals(id, openId, StringComparison.Ordinal)));
        }
        return list;
    }

    /// <summary>
    /// Writes live plan/section ink into the open named map so Ctrl+S / ZIP / cloud keep documents in sync.
    /// </summary>
    public static bool SyncLiveIntoActive(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var openId = project.ActiveCartographyMapId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(openId) || !TryGetMaps(project, out var maps))
            return false;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            var found = false;
            foreach (var map in maps.EnumerateArray())
            {
                if (!string.Equals(ReadString(map, "id"), openId, StringComparison.Ordinal))
                {
                    map.WriteTo(writer);
                    continue;
                }

                found = true;
                writer.WriteStartObject();
                foreach (var prop in map.EnumerateObject())
                {
                    if (LayerNames.Contains(prop.Name))
                        continue;
                    prop.WriteTo(writer);
                }

                WriteLayer(writer, "sketches", map, project.Sketches);
                WriteLayer(writer, "sectionSketches", map, project.SectionSketches);
                WriteLayer(writer, "mapSymbols", map, project.MapSymbols);
                WriteLayer(writer, "vectorLines", map, project.VectorLines);
                WriteLayer(writer, "brackets", map, project.Brackets);
                WriteLayer(writer, "depthSpanAnnotations", map, project.DepthSpanAnnotations);
                writer.WriteNumber("updatedAtEpochMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (!found)
                return false;
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        project.NamedCartographyMaps = doc.RootElement.Clone();
        return true;
    }

    public static bool TryOpen(CaveProjectDocument project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(id))
            return false;
        SyncLiveIntoActive(project);
        if (!TryGetMaps(project, out var maps))
            return false;
        foreach (var map in maps.EnumerateArray())
        {
            if (!string.Equals(ReadString(map, "id"), id, StringComparison.Ordinal))
                continue;
            CopyArray(map, "sketches", value => project.Sketches = value);
            CopyArray(map, "sectionSketches", value => project.SectionSketches = value);
            CopyArray(map, "mapSymbols", value => project.MapSymbols = value);
            CopyArray(map, "vectorLines", value => project.VectorLines = value);
            CopyArray(map, "brackets", value => project.Brackets = value);
            CopyArray(map, "depthSpanAnnotations", value => project.DepthSpanAnnotations = value);
            project.ActiveCartographyMapId = id;
            return true;
        }

        return false;
    }

    /// <summary>Captures the open map, then starts a blank named document.</summary>
    public static SaveResult TryCreateEmpty(CaveProjectDocument project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        // Preserve any unlabeled drawings before clearing the canvas.
        EnsureNamedDocuments(project);
        SyncLiveIntoActive(project);
        return AppendNewMap(project, name, copyLive: false, clearLive: true);
    }

    /// <summary>Captures the open map, then duplicates live drawings as a new named document.</summary>
    public static SaveResult TrySaveAs(CaveProjectDocument project, string name)
    {
        ArgumentNullException.ThrowIfNull(project);
        SyncLiveIntoActive(project);
        return AppendNewMap(project, name, copyLive: true, clearLive: false);
    }

    public static string? TryRename(CaveProjectDocument project, string id, string desired)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(id) || !TryGetMaps(project, out var maps))
            return null;

        var unique = UniqueName(project, desired, id);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var found = false;
        project.NamedCartographyMaps = BuildMapsJson(writer =>
        {
            foreach (var map in maps.EnumerateArray())
            {
                if (!string.Equals(ReadString(map, "id"), id, StringComparison.Ordinal))
                {
                    map.WriteTo(writer);
                    continue;
                }

                found = true;
                writer.WriteStartObject();
                foreach (var prop in map.EnumerateObject())
                {
                    if (prop.Name is "name" or "updatedAtEpochMs")
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WriteString("name", unique);
                writer.WriteNumber("updatedAtEpochMs", now);
                writer.WriteEndObject();
            }
        });
        return found ? unique : null;
    }

    public static DeleteResult? TryDelete(CaveProjectDocument project, string id)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(id) || !TryGetMaps(project, out var maps))
            return null;

        var wasOpen = string.Equals(project.ActiveCartographyMapId?.Trim(), id, StringComparison.Ordinal);
        if (!wasOpen)
            SyncLiveIntoActive(project);
        if (!TryGetMaps(project, out maps))
            return null;

        var remaining = new List<JsonElement>();
        var found = false;
        foreach (var map in maps.EnumerateArray())
        {
            if (string.Equals(ReadString(map, "id"), id, StringComparison.Ordinal))
            {
                found = true;
                continue;
            }

            remaining.Add(map.Clone());
        }

        if (!found)
            return null;

        project.NamedCartographyMaps = BuildMapsJson(writer =>
        {
            foreach (var map in remaining)
                map.WriteTo(writer);
        });

        if (!wasOpen)
        {
            var open = List(project).FirstOrDefault(item => item.IsOpen);
            return new DeleteResult(
                string.IsNullOrWhiteSpace(open.Id) ? null : open.Id,
                string.IsNullOrWhiteSpace(open.Name) ? null : open.Name);
        }

        if (remaining.Count == 0)
        {
            project.ActiveCartographyMapId = "";
            ClearLive(project);
            return new DeleteResult(null, null);
        }

        var next = remaining[0];
        var best = ReadUpdatedAt(next);
        foreach (var map in remaining)
        {
            var updated = ReadUpdatedAt(map);
            if (updated < best)
                continue;
            best = updated;
            next = map;
        }

        var nextId = ReadString(next, "id");
        TryOpen(project, nextId);
        return new DeleteResult(nextId, ReadString(next, "name"));
    }

    private static SaveResult AppendNewMap(CaveProjectDocument project, string name, bool copyLive, bool clearLive)
    {
        var unique = UniqueName(project, name, exceptId: null);
        var id = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existing = SnapshotMaps(project);
        project.NamedCartographyMaps = BuildMapsJson(writer =>
        {
            foreach (var map in existing)
                map.WriteTo(writer);
            WriteNewMap(writer, id, unique, now, copyLive ? project : null);
        });
        project.ActiveCartographyMapId = id;
        if (clearLive)
            ClearLive(project);
        return new SaveResult(id, unique, Created: true);
    }

    private static string UniqueName(CaveProjectDocument project, string desired, string? exceptId)
    {
        var baseName = desired.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = string.IsNullOrWhiteSpace(project.Name) ? "Plan" : project.Name.Trim();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in SnapshotMaps(project))
        {
            if (exceptId != null && string.Equals(ReadString(map, "id"), exceptId, StringComparison.Ordinal))
                continue;
            var existing = ReadString(map, "name").Trim();
            if (!string.IsNullOrEmpty(existing))
                taken.Add(existing);
        }

        if (!taken.Contains(baseName))
            return baseName;
        var n = 2;
        while (taken.Contains($"{baseName} {n}"))
            n++;
        return $"{baseName} {n}";
    }

    private static List<JsonElement> SnapshotMaps(CaveProjectDocument project)
    {
        var list = new List<JsonElement>();
        if (!TryGetMaps(project, out var maps))
            return list;
        foreach (var map in maps.EnumerateArray())
            list.Add(map.Clone());
        return list;
    }

    private static JsonElement BuildMapsJson(Action<Utf8JsonWriter> writeItems)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writeItems(writer);
            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteNewMap(
        Utf8JsonWriter writer,
        string id,
        string name,
        long nowMs,
        CaveProjectDocument? liveSource)
    {
        writer.WriteStartObject();
        writer.WriteString("id", id);
        writer.WriteString("name", name);
        var empty = default(JsonElement);
        WriteLayer(writer, "sketches", empty, liveSource?.Sketches);
        WriteLayer(writer, "sectionSketches", empty, liveSource?.SectionSketches);
        WriteLayer(writer, "mapSymbols", empty, liveSource?.MapSymbols);
        WriteLayer(writer, "vectorLines", empty, liveSource?.VectorLines);
        WriteLayer(writer, "brackets", empty, liveSource?.Brackets);
        WriteLayer(writer, "depthSpanAnnotations", empty, liveSource?.DepthSpanAnnotations);
        writer.WriteNumber("updatedAtEpochMs", nowMs);
        writer.WriteEndObject();
    }

    private static void ClearLive(CaveProjectDocument project)
    {
        project.Sketches = EmptyArray.Clone();
        project.SectionSketches = EmptyArray.Clone();
        project.MapSymbols = EmptyArray.Clone();
        project.VectorLines = EmptyArray.Clone();
        project.Brackets = EmptyArray.Clone();
        project.DepthSpanAnnotations = EmptyArray.Clone();
    }

    private static long ReadUpdatedAt(JsonElement map)
    {
        if (map.TryGetProperty("updatedAtEpochMs", out var el) &&
            el.ValueKind == JsonValueKind.Number &&
            el.TryGetInt64(out var value))
            return value;
        return 0;
    }

    private static bool TryGetMaps(CaveProjectDocument project, out JsonElement maps)
    {
        if (project.NamedCartographyMaps.ValueKind == JsonValueKind.Array)
        {
            maps = project.NamedCartographyMaps;
            return true;
        }

        if (project.ExtensionData != null &&
            project.ExtensionData.TryGetValue("namedCartographyMaps", out maps) &&
            maps.ValueKind == JsonValueKind.Array)
            return true;
        maps = default;
        return false;
    }

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static void CopyArray(JsonElement map, string name, Action<JsonElement> assign)
    {
        if (map.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            assign(el.Clone());
        else
            assign(EmptyArray.Clone());
    }

    private static void WriteLayer(Utf8JsonWriter writer, string name, JsonElement map, JsonElement? live)
    {
        writer.WritePropertyName(name);
        if (live is { } value && value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            value.WriteTo(writer);
        else if (map.ValueKind == JsonValueKind.Object &&
                 map.TryGetProperty(name, out var existing) &&
                 existing.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
            existing.WriteTo(writer);
        else
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
    }

    private static JsonElement ParseClone(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
