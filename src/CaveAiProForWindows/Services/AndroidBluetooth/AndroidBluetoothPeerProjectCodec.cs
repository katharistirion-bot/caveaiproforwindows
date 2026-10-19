using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.AndroidBluetooth;

public enum AndroidBluetoothPeerMergeMode
{
    ShotsOnly,
    ShotsAndBrackets,
    AllNoMedia,
}

public readonly record struct AndroidBluetoothPeerMergeResult(
    int ShotsAdded,
    int BracketsAdded,
    int DepthSpansAdded,
    int RocksAdded);

/// <summary>
/// Maps Android peer snapshot JSON onto <see cref="CaveProjectDocument"/> without touching ZIP/cloud loaders.
/// Photos and audio URIs are stripped — same no-media contract as phone-to-phone exchange.
/// </summary>
public static class AndroidBluetoothPeerProjectCodec
{
    public static string BuildSnapshotJson(CaveProjectDocument project, string safety, string nodeId = AndroidBluetoothPeerProtocol.WindowsNodeId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var clone = CloneWithoutMedia(project);
        var projectJson = SurveyPortableZipExporter.SerializeSingleProjectObject(clone);
        var envelope = new JsonObject
        {
            ["type"] = AndroidBluetoothPeerProtocol.TypeSessionSnapshot,
            ["nodeId"] = nodeId,
            ["safety"] = string.IsNullOrWhiteSpace(safety) ? "NOMINAL" : safety,
            ["timestampMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["platformOrigin"] = AndroidBluetoothPeerProtocol.PlatformOriginWindows,
            ["project"] = JsonNode.Parse(projectJson),
        };
        return envelope.ToJsonString();
    }

    public static CaveProjectDocument ParseProjectJson(string projectJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectJson);
        var list = ExplorationDataLoader.DeserializeProjectsFromText(projectJson);
        if (list.Count == 0)
            throw new InvalidDataException("Peer session contained no survey project.");
        var project = list[0];
        StripMedia(project);
        if (project.Shots.Count > 50_000)
            throw new InvalidDataException("Peer session has too many shots.");
        return project;
    }

    public static CaveProjectDocument CloneWithoutMedia(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var json = SurveyPortableZipExporter.SerializeSingleProjectObject(project);
        var clone = ParseProjectJson(json);
        return clone;
    }

    public static void StripMedia(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var shot in project.Shots)
        {
            shot.Photos.Clear();
            shot.AudioMemoUri = null;
        }

        project.Rocks = BlankObjectArrayProperty(project.Rocks, "imageUri");
        project.FieldCatalogEntries = BlankObjectArrayProperty(project.FieldCatalogEntries, "photoReference");
        project.CartographyTlsMeshObjUri = null;
        project.SurfaceLidarRaster = BlankObjectProperty(project.SurfaceLidarRaster, "imageUri");
        project.PublicLibraryCartographyUris = JsonSerializer.SerializeToElement(Array.Empty<string>());
    }

    /// <summary>Append shots whose ids are not already in <paramref name="target"/>. Never overwrites existing legs.</summary>
    public static int MergeShotsOnly(CaveProjectDocument target, CaveProjectDocument incoming) =>
        Merge(target, incoming, AndroidBluetoothPeerMergeMode.ShotsOnly).ShotsAdded;

    /// <summary>
    /// Android merge modes: SHOTS_ONLY, SHOTS_AND_BRACKETS, ALL_NO_MEDIA.
    /// Incoming media URIs are always dropped.
    /// </summary>
    public static AndroidBluetoothPeerMergeResult Merge(
        CaveProjectDocument target,
        CaveProjectDocument incoming,
        AndroidBluetoothPeerMergeMode mode)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);
        StripMedia(incoming);

        var shotsAdded = MergeShotsInto(target, incoming);
        var bracketsAdded = 0;
        var depthAdded = 0;
        var rocksAdded = 0;

        if (mode == AndroidBluetoothPeerMergeMode.ShotsOnly)
            return new AndroidBluetoothPeerMergeResult(shotsAdded, 0, 0, 0);

        bracketsAdded = CountNewIds(target.Brackets, incoming.Brackets);
        depthAdded = CountNewIds(target.DepthSpanAnnotations, incoming.DepthSpanAnnotations);
        target.Brackets = MergeObjectArrayById(target.Brackets, incoming.Brackets);
        target.DepthSpanAnnotations = MergeObjectArrayById(target.DepthSpanAnnotations, incoming.DepthSpanAnnotations);

        if (mode == AndroidBluetoothPeerMergeMode.ShotsAndBrackets)
            return new AndroidBluetoothPeerMergeResult(shotsAdded, bracketsAdded, depthAdded, 0);

        rocksAdded = CountNewIds(projectArray(target.Rocks), projectArray(incoming.Rocks));
        target.Rocks = MergeObjectArrayById(projectArray(target.Rocks), projectArray(incoming.Rocks), "imageUri");
        target.TrackPoints = ConcatArrays(target.TrackPoints, incoming.TrackPoints);

        var incomingHadNamed = HasNamedMaps(incoming);
        var targetHadNamed = HasNamedMaps(target);
        if (incomingHadNamed || targetHadNamed)
        {
            target.NamedCartographyMaps = MergeNamedCartographyMaps(target.NamedCartographyMaps, incoming.NamedCartographyMaps);
            if (string.IsNullOrWhiteSpace(target.ActiveCartographyMapId))
                target.ActiveCartographyMapId = incoming.ActiveCartographyMapId;
            NamedCartographyDocuments.CopyActiveDocumentToLive(target);
        }
        else
        {
            target.Sketches = ConcatArrays(target.Sketches, incoming.Sketches);
            target.SectionSketches = ConcatArrays(target.SectionSketches, incoming.SectionSketches);
            target.MapSymbols = ConcatArrays(target.MapSymbols, incoming.MapSymbols);
            if (incoming.VectorLines is { } incomingVectors)
            {
                var targetVectors = target.VectorLines ?? default;
                target.VectorLines = ConcatArrays(targetVectors, incomingVectors);
            }
        }

        NamedCartographyDocuments.EnsureNamedDocuments(target);
        return new AndroidBluetoothPeerMergeResult(shotsAdded, bracketsAdded, depthAdded, rocksAdded);
    }

    private static JsonElement projectArray(JsonElement? el) =>
        el is { ValueKind: JsonValueKind.Array } v ? v : JsonSerializer.SerializeToElement(Array.Empty<object>());

    private static int MergeShotsInto(CaveProjectDocument target, CaveProjectDocument incoming)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shot in target.Shots)
        {
            if (!string.IsNullOrWhiteSpace(shot.Id))
                existing.Add(shot.Id);
        }

        var added = 0;
        foreach (var shot in incoming.Shots)
        {
            if (string.IsNullOrWhiteSpace(shot.Id) || !existing.Add(shot.Id))
                continue;
            shot.Photos.Clear();
            shot.AudioMemoUri = null;
            target.Shots.Add(shot);
            added++;
        }

        return added;
    }

    private static bool HasNamedMaps(CaveProjectDocument project) =>
        project.NamedCartographyMaps.ValueKind == JsonValueKind.Array &&
        project.NamedCartographyMaps.GetArrayLength() > 0;

    private static int CountNewIds(JsonElement existing, JsonElement incoming)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(existing, seen);
        var added = 0;
        if (incoming.ValueKind != JsonValueKind.Array)
            return 0;
        foreach (var item in incoming.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = ReadId(item);
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;
            added++;
        }

        return added;
    }

    private static void CollectIds(JsonElement arr, HashSet<string> seen)
    {
        if (arr.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in arr.EnumerateArray())
        {
            var id = ReadId(item);
            if (!string.IsNullOrWhiteSpace(id))
                seen.Add(id);
        }
    }

    private static string ReadId(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return "";
        return item.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
    }

    private static JsonElement MergeObjectArrayById(JsonElement existing, JsonElement incoming, string? blankIncomingProperty = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            WriteObjects(writer, existing, seen, incomingSide: false, blankIncomingProperty);
            WriteObjects(writer, incoming, seen, incomingSide: true, blankIncomingProperty);
            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteObjects(
        Utf8JsonWriter writer,
        JsonElement arr,
        HashSet<string> seen,
        bool incomingSide,
        string? blankIncomingProperty)
    {
        if (arr.ValueKind != JsonValueKind.Array)
            return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var id = ReadId(item);
            if (!string.IsNullOrWhiteSpace(id) && !seen.Add(id))
                continue;
            if (incomingSide && !string.IsNullOrEmpty(blankIncomingProperty))
            {
                writer.WriteStartObject();
                foreach (var prop in item.EnumerateObject())
                {
                    if (prop.NameEquals(blankIncomingProperty))
                        writer.WriteString(prop.Name, "");
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            else
            {
                item.WriteTo(writer);
            }
        }
    }

    private static JsonElement ConcatArrays(JsonElement a, JsonElement b)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            if (a.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in a.EnumerateArray())
                    el.WriteTo(writer);
            }

            if (b.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in b.EnumerateArray())
                    el.WriteTo(writer);
            }

            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static JsonElement MergeNamedCartographyMaps(JsonElement existing, JsonElement incoming)
    {
        var byId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var order = new List<string>();

        void ingest(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array)
                return;
            foreach (var map in arr.EnumerateArray())
            {
                if (map.ValueKind != JsonValueKind.Object)
                    continue;
                var id = ReadId(map);
                if (string.IsNullOrWhiteSpace(id))
                    id = Guid.NewGuid().ToString("D");
                var ts = ReadUpdatedAt(map);
                if (byId.TryGetValue(id, out var cur))
                {
                    if (ts >= ReadUpdatedAt(cur))
                        byId[id] = map.Clone();
                }
                else
                {
                    byId[id] = map.Clone();
                    order.Add(id);
                }
            }
        }

        ingest(existing);
        ingest(incoming);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var id in order)
                byId[id].WriteTo(writer);
            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static long ReadUpdatedAt(JsonElement map)
    {
        if (map.TryGetProperty("updatedAtEpochMs", out var el) && el.TryGetInt64(out var v))
            return v;
        return 0;
    }

    private static JsonElement? BlankObjectArrayProperty(JsonElement? arr, string propertyName)
    {
        if (arr is not { ValueKind: JsonValueKind.Array })
            return arr;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var item in arr.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    item.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (var prop in item.EnumerateObject())
                {
                    if (prop.NameEquals(propertyName))
                        writer.WriteString(prop.Name, "");
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static JsonElement BlankObjectProperty(JsonElement obj, string propertyName)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return obj;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.NameEquals(propertyName))
                    writer.WriteString(prop.Name, "");
                else
                    prop.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }
}
