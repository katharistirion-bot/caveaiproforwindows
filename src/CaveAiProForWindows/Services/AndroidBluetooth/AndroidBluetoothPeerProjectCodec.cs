using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.AndroidBluetooth;

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
    }

    /// <summary>Append shots whose ids are not already in <paramref name="target"/>. Never overwrites existing legs.</summary>
    public static int MergeShotsOnly(CaveProjectDocument target, CaveProjectDocument incoming)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(incoming);
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
}
