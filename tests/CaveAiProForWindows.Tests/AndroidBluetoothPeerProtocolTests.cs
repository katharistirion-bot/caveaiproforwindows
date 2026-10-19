using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.AndroidBluetooth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class AndroidBluetoothPeerProtocolTests
{
    [TestMethod]
    public void Constants_match_android_peer_rfcomm_contract()
    {
        Assert.AreEqual(Guid.Parse("3d2e2d9e-0e3a-4e5f-8c7a-2b3b0c6c9b10"), AndroidBluetoothPeerProtocol.RfcommServiceUuid);
        Assert.AreEqual("CaveAIInfoExchange", AndroidBluetoothPeerProtocol.ServiceName);
        Assert.AreEqual("ACK", AndroidBluetoothPeerProtocol.AckToken);
        Assert.AreEqual(6000, AndroidBluetoothPeerProtocol.SessionChunkSize);
        Assert.AreEqual(2048, AndroidBluetoothPeerProtocol.MaxTotalChunks);
        Assert.AreEqual(16 * 1024 * 1024, AndroidBluetoothPeerProtocol.MaxDecodedSnapshotBytes);
    }

    [TestMethod]
    public void Session_chunks_round_trip_project_json()
    {
        var project = new CaveProjectDocument
        {
            Name = "BT Peer Cave",
            Date = "2026-09-05",
            Alt = 120,
            Shots =
            [
                new ShotRecord
                {
                    Id = "shot-a1",
                    FromStation = "A1",
                    ToStation = "A2",
                    Distance = 4.5f,
                    Azimuth = 90,
                    Clino = -12,
                    Photos = ["content://should-be-stripped"],
                    AudioMemoUri = "file://memo",
                },
            ],
        };

        var snapshot = AndroidBluetoothPeerProjectCodec.BuildSnapshotJson(project, "NOMINAL");
        var lines = AndroidBluetoothPeerProtocol.BuildSessionChunkLines(snapshot, "NOMINAL", msgId: "msg-1");
        Assert.IsTrue(lines.Count >= 1);

        var assembler = new AndroidBluetoothPeerSessionAssembler();
        AndroidBluetoothPeerDecodedSnapshot? decoded = null;
        foreach (var line in lines)
        {
            Assert.IsTrue(AndroidBluetoothPeerProtocol.TryParseJsonLine(line, out var obj));
            Assert.AreEqual(AndroidBluetoothPeerProtocol.TypeSessionChunk, AndroidBluetoothPeerProtocol.GetType(obj!));
            Assert.IsTrue(assembler.TryAddChunk(obj!, out var snap, out var error), error);
            if (snap != null)
                decoded = snap;
        }

        Assert.IsNotNull(decoded);
        var loaded = AndroidBluetoothPeerProjectCodec.ParseProjectJson(decoded.Value.ProjectJson);
        Assert.AreEqual("BT Peer Cave", loaded.Name);
        Assert.AreEqual(1, loaded.Shots.Count);
        Assert.AreEqual("A1", loaded.Shots[0].FromStation);
        Assert.AreEqual(0, loaded.Shots[0].Photos.Count);
        Assert.IsTrue(string.IsNullOrEmpty(loaded.Shots[0].AudioMemoUri));
    }

    [TestMethod]
    public void Merge_shots_only_never_overwrites_existing_ids()
    {
        var target = new CaveProjectDocument
        {
            Name = "Office",
            Shots =
            [
                new ShotRecord { Id = "keep", FromStation = "A1", ToStation = "A2", Distance = 1 },
            ],
        };
        var incoming = new CaveProjectDocument
        {
            Name = "Field",
            Shots =
            [
                new ShotRecord { Id = "keep", FromStation = "X", ToStation = "Y", Distance = 99 },
                new ShotRecord { Id = "new", FromStation = "A2", ToStation = "A3", Distance = 2 },
            ],
        };

        var added = AndroidBluetoothPeerProjectCodec.MergeShotsOnly(target, incoming);
        Assert.AreEqual(1, added);
        Assert.AreEqual(2, target.Shots.Count);
        Assert.AreEqual("A1", target.Shots[0].FromStation);
        Assert.AreEqual(1f, target.Shots[0].Distance);
        Assert.AreEqual("new", target.Shots[1].Id);
        Assert.AreEqual(0, target.Shots[1].Photos.Count);
    }

    [TestMethod]
    public void Merge_shots_only_strips_foreign_content_uris()
    {
        var target = new CaveProjectDocument { Name = "Office" };
        var incoming = new CaveProjectDocument
        {
            Name = "Field",
            Shots =
            [
                new ShotRecord
                {
                    Id = "new",
                    FromStation = "A2",
                    ToStation = "A3",
                    Photos = ["content://media/external/images/1"],
                    AudioMemoUri = "content://media/external/audio/9",
                },
            ],
        };

        var added = AndroidBluetoothPeerProjectCodec.MergeShotsOnly(target, incoming);
        Assert.AreEqual(1, added);
        Assert.AreEqual(0, target.Shots[0].Photos.Count);
        Assert.IsTrue(string.IsNullOrEmpty(target.Shots[0].AudioMemoUri));
    }

    [TestMethod]
    public void Merge_shots_and_brackets_appends_new_annotation_ids()
    {
        var target = new CaveProjectDocument
        {
            Name = "Office",
            Shots = [new ShotRecord { Id = "keep", FromStation = "A1", ToStation = "A2" }],
            Brackets = JsonDocument.Parse("""[{"id":"b0","x":1,"y":2}]""").RootElement.Clone(),
        };
        var incoming = new CaveProjectDocument
        {
            Name = "Field",
            Shots = [new ShotRecord { Id = "new", FromStation = "A2", ToStation = "A3" }],
            Brackets = JsonDocument.Parse("""[{"id":"b0","x":9},{"id":"b1","x":3,"y":4}]""").RootElement.Clone(),
            DepthSpanAnnotations = JsonDocument.Parse("""[{"id":"d1","depthMeters":12}]""").RootElement.Clone(),
        };

        var result = AndroidBluetoothPeerProjectCodec.Merge(
            target, incoming, AndroidBluetoothPeerMergeMode.ShotsAndBrackets);
        Assert.AreEqual(1, result.ShotsAdded);
        Assert.AreEqual(1, result.BracketsAdded);
        Assert.AreEqual(1, result.DepthSpansAdded);
        Assert.AreEqual(2, target.Brackets.GetArrayLength());
        Assert.AreEqual(1, target.DepthSpanAnnotations.GetArrayLength());
        Assert.AreEqual("A1", target.Shots[0].FromStation);
    }

    [TestMethod]
    public void Merge_all_no_media_merges_rocks_without_image_uris()
    {
        var target = new CaveProjectDocument
        {
            Name = "Office",
            Rocks = JsonDocument.Parse("""[{"id":"r0","imageUri":"keep","station":"A1"}]""").RootElement.Clone(),
        };
        var incoming = new CaveProjectDocument
        {
            Name = "Field",
            Shots = [new ShotRecord { Id = "s1", Photos = ["content://x"] }],
            Rocks = JsonDocument.Parse(
                """[{"id":"r0","imageUri":"content://peer"},{"id":"r1","imageUri":"content://new","station":"A2"}]""")
                .RootElement.Clone(),
        };

        var result = AndroidBluetoothPeerProjectCodec.Merge(
            target, incoming, AndroidBluetoothPeerMergeMode.AllNoMedia);
        Assert.AreEqual(1, result.ShotsAdded);
        Assert.AreEqual(1, result.RocksAdded);
        Assert.AreEqual(2, target.Rocks!.Value.GetArrayLength());
        Assert.AreEqual("keep", target.Rocks.Value[0].GetProperty("imageUri").GetString());
        Assert.AreEqual("", target.Rocks.Value[1].GetProperty("imageUri").GetString());
        Assert.AreEqual(0, target.Shots[0].Photos.Count);
    }

    [TestMethod]
    public void Safety_line_is_ack_protocol_json()
    {
        var line = AndroidBluetoothPeerProtocol.BuildSafetyLine("WARNING", "B4", 18.5, "low air", 1_700_000_000_000);
        Assert.IsFalse(AndroidBluetoothPeerProtocol.IsAckLine(line));
        Assert.IsTrue(AndroidBluetoothPeerProtocol.TryParseJsonLine(line, out var obj));
        var msg = AndroidBluetoothPeerSafetyMessage.FromJson(obj!);
        Assert.AreEqual("windows", msg.NodeId);
        Assert.AreEqual("WARNING", msg.Safety);
        Assert.AreEqual("B4", msg.Station);
        Assert.AreEqual(18.5, msg.Depth);
    }

    [TestMethod]
    public void Ack_token_matches_android()
    {
        Assert.IsTrue(AndroidBluetoothPeerProtocol.IsAckLine("ACK"));
        Assert.IsTrue(AndroidBluetoothPeerProtocol.IsAckLine(" ACK \n".Trim()));
        Assert.IsFalse(AndroidBluetoothPeerProtocol.IsAckLine("CAVEAI_PEER"));
    }
}
