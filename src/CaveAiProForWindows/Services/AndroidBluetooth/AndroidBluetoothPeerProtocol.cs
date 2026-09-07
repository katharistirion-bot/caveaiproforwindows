using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CaveAiProForWindows.Services.AndroidBluetooth;

/// <summary>
/// Line protocol shared with Android <c>PeerSurveyBluetoothExchange</c> / <c>PeerExchangeLimits</c>.
/// Do not change framing constants without a matching Android release.
/// </summary>
public static class AndroidBluetoothPeerProtocol
{
    public static readonly Guid RfcommServiceUuid = Guid.Parse("3d2e2d9e-0e3a-4e5f-8c7a-2b3b0c6c9b10");
    public const string ServiceName = "CaveAIInfoExchange";
    public const string AckToken = "ACK";
    public const string TypeSafety = "CAVEAI_PEER";
    public const string TypeSessionChunk = "CAVEAI_PEER_SESSION_CHUNK";
    public const string TypeSessionSnapshot = "CAVEAI_PEER_SESSION_SNAPSHOT";
    public const string WindowsNodeId = "windows";
    public const string PlatformOriginWindows = "windows";

    public const int SessionChunkSize = 6000;
    public const int MaxTotalChunks = 2048;
    public const int MaxLineUtf8Chars = 262_144;
    public const int MaxDecodedSnapshotBytes = 16 * 1024 * 1024;
    public const int AckTimeoutMs = 5_000;
    public const int AckMaxRetransmissions = 3;
    public const int ConnectMaxAttempts = 3;
    public const int ConnectRetryDelayMs = 500;
    public const int InterChunkDelayMs = 20;
    public const long SessionChunkExpiryMs = 120_000;

    public static bool IsAckLine(string line) =>
        string.Equals(line.Trim(), AckToken, StringComparison.Ordinal);

    public static string BuildSafetyLine(
        string safety,
        string station,
        double depth,
        string statusText,
        long timestampMs,
        string nodeId = WindowsNodeId)
    {
        var obj = new JsonObject
        {
            ["type"] = TypeSafety,
            ["nodeId"] = nodeId,
            ["safety"] = string.IsNullOrWhiteSpace(safety) ? "NOMINAL" : safety,
            ["station"] = station ?? "",
            ["depth"] = depth,
            ["statusText"] = statusText ?? "",
            ["timestampMs"] = timestampMs,
            ["platformOrigin"] = PlatformOriginWindows,
        };
        return obj.ToJsonString();
    }

    public static IReadOnlyList<string> BuildSessionChunkLines(
        string snapshotJson,
        string safety,
        string nodeId = WindowsNodeId,
        long? timestampMs = null,
        string? msgId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotJson);
        var bytes = Encoding.UTF8.GetBytes(snapshotJson);
        if (bytes.Length > MaxDecodedSnapshotBytes)
            throw new InvalidOperationException("Session too large to send over Bluetooth (export a ZIP instead).");

        var snapshotBase64 = Convert.ToBase64String(bytes);
        var totalChunks = (snapshotBase64.Length + SessionChunkSize - 1) / SessionChunkSize;
        if (totalChunks > MaxTotalChunks ||
            snapshotBase64.Length > (long)MaxTotalChunks * SessionChunkSize)
        {
            throw new InvalidOperationException("Session too large to send over Bluetooth (export a ZIP instead).");
        }

        var id = string.IsNullOrWhiteSpace(msgId) ? Guid.NewGuid().ToString() : msgId;
        var ts = timestampMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lines = new List<string>(Math.Max(1, totalChunks));
        if (totalChunks == 0)
        {
            lines.Add(BuildChunkLine(id, 0, 1, nodeId, safety, ts, ""));
            return lines;
        }

        for (var i = 0; i < totalChunks; i++)
        {
            var start = i * SessionChunkSize;
            var len = Math.Min(SessionChunkSize, snapshotBase64.Length - start);
            var chunk = snapshotBase64.Substring(start, len);
            lines.Add(BuildChunkLine(id, i, totalChunks, nodeId, safety, ts, chunk));
        }

        return lines;
    }

    private static string BuildChunkLine(
        string msgId,
        int index,
        int totalChunks,
        string nodeId,
        string safety,
        long timestampMs,
        string dataChunk)
    {
        var obj = new JsonObject
        {
            ["type"] = TypeSessionChunk,
            ["msgId"] = msgId,
            ["index"] = index,
            ["totalChunks"] = totalChunks,
            ["nodeId"] = nodeId,
            ["safety"] = string.IsNullOrWhiteSpace(safety) ? "NOMINAL" : safety,
            ["timestampMs"] = timestampMs,
            ["dataChunk"] = dataChunk,
            ["platformOrigin"] = PlatformOriginWindows,
        };
        return obj.ToJsonString();
    }

    public static bool TryParseJsonLine(string line, out JsonObject? obj)
    {
        obj = null;
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineUtf8Chars)
            return false;
        try
        {
            obj = JsonNode.Parse(line) as JsonObject;
            return obj != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string GetType(JsonObject obj) =>
        obj["type"]?.GetValue<string>()?.Trim().ToUpperInvariant() ?? "";

    public static int GetInt(JsonNode? node, int fallback)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return (int)l;
            if (v.TryGetValue<double>(out var d)) return (int)d;
        }

        return fallback;
    }

    public static long GetLong(JsonNode? node, long fallback)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d)) return (long)d;
        }

        return fallback;
    }

    public static double? TryGetDouble(JsonNode? node)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return l;
        }

        return null;
    }
}

/// <summary>Reassembles Android <c>CAVEAI_PEER_SESSION_CHUNK</c> payloads.</summary>
public sealed class AndroidBluetoothPeerSessionAssembler
{
    private readonly Dictionary<string, Incoming> _byMsgId = new(StringComparer.Ordinal);

    public bool TryAddChunk(JsonObject json, out AndroidBluetoothPeerDecodedSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        error = null;
        var msgId = json["msgId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(msgId))
        {
            error = "Missing msgId.";
            return false;
        }

        var totalChunks = AndroidBluetoothPeerProtocol.GetInt(json["totalChunks"], 0);
        var index = AndroidBluetoothPeerProtocol.GetInt(json["index"], -1);
        var dataChunk = json["dataChunk"]?.GetValue<string>() ?? "";
        var nodeId = json["nodeId"]?.GetValue<string>() ?? "unknown";
        var safety = json["safety"]?.GetValue<string>() ?? "NOMINAL";
        var ts = AndroidBluetoothPeerProtocol.GetLong(json["timestampMs"], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (totalChunks <= 0 || totalChunks > AndroidBluetoothPeerProtocol.MaxTotalChunks)
        {
            _byMsgId.Remove(msgId);
            error = "Chunk count exceeds Bluetooth peer limit.";
            return false;
        }

        if (dataChunk.Length > AndroidBluetoothPeerProtocol.MaxLineUtf8Chars)
        {
            _byMsgId.Remove(msgId);
            error = "Chunk too large.";
            return false;
        }

        if (!_byMsgId.TryGetValue(msgId, out var incoming))
        {
            if (index < 0)
            {
                error = "Invalid chunk index.";
                return false;
            }

            incoming = new Incoming(nodeId, safety, totalChunks, ts);
            _byMsgId[msgId] = incoming;
        }

        if (ts - incoming.FirstReceivedAtMs > AndroidBluetoothPeerProtocol.SessionChunkExpiryMs)
        {
            _byMsgId.Remove(msgId);
            error = "Session transfer timed out.";
            return false;
        }

        if (index < 0 || index >= incoming.Chunks.Length)
            return true;
        if (dataChunk.Length == 0)
            return true;

        incoming.Chunks[index] = dataChunk;
        if (incoming.Chunks.Any(string.IsNullOrEmpty))
            return true;

        _byMsgId.Remove(msgId);
        try
        {
            snapshot = DecodeCompleted(incoming);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static AndroidBluetoothPeerDecodedSnapshot DecodeCompleted(Incoming incoming)
    {
        var fullBase64 = string.Concat(incoming.Chunks);
        var maxChars = (long)AndroidBluetoothPeerProtocol.MaxTotalChunks *
                       AndroidBluetoothPeerProtocol.MaxLineUtf8Chars;
        if (fullBase64.Length > maxChars)
            throw new InvalidOperationException("Peer session payload too large.");

        byte[] decodedBytes;
        try
        {
            decodedBytes = Convert.FromBase64String(fullBase64.Replace("\r", "").Replace("\n", "").Replace(" ", ""));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Peer session payload is not valid Base64.");
        }

        if (decodedBytes.Length > AndroidBluetoothPeerProtocol.MaxDecodedSnapshotBytes)
            throw new InvalidOperationException("Peer session payload too large.");

        var decodedText = Encoding.UTF8.GetString(decodedBytes);
        using var doc = JsonDocument.Parse(decodedText);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("project", out var projectEl) ||
            projectEl.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Peer session JSON is missing project.");
        }

        return new AndroidBluetoothPeerDecodedSnapshot(
            ProjectJson: projectEl.GetRawText(),
            NodeId: incoming.NodeId,
            Safety: incoming.Safety,
            TimestampMs: incoming.FirstReceivedAtMs);
    }

    private sealed class Incoming
    {
        public Incoming(string nodeId, string safety, int totalChunks, long firstReceivedAtMs)
        {
            NodeId = nodeId;
            Safety = safety;
            Chunks = new string[totalChunks];
            Array.Fill(Chunks, "");
            FirstReceivedAtMs = firstReceivedAtMs;
        }

        public string NodeId { get; }
        public string Safety { get; }
        public string[] Chunks { get; }
        public long FirstReceivedAtMs { get; }
    }
}

public readonly record struct AndroidBluetoothPeerDecodedSnapshot(
    string ProjectJson,
    string NodeId,
    string Safety,
    long TimestampMs);

public sealed class AndroidBluetoothPeerSafetyMessage
{
    public required string NodeId { get; init; }
    public required string Safety { get; init; }
    public string? Station { get; init; }
    public double? Depth { get; init; }
    public string? StatusText { get; init; }
    public long TimestampMs { get; init; }

    public static AndroidBluetoothPeerSafetyMessage FromJson(JsonObject json)
    {
        var depth = AndroidBluetoothPeerProtocol.TryGetDouble(json["depth"]);

        var station = json["station"]?.GetValue<string>();
        var status = json["statusText"]?.GetValue<string>();
        return new AndroidBluetoothPeerSafetyMessage
        {
            NodeId = json["nodeId"]?.GetValue<string>() ?? "unknown",
            Safety = json["safety"]?.GetValue<string>() ?? "NOMINAL",
            Station = string.IsNullOrWhiteSpace(station) || station == "null" ? null : station,
            Depth = depth,
            StatusText = string.IsNullOrWhiteSpace(status) || status == "null" ? null : status,
            TimestampMs = AndroidBluetoothPeerProtocol.GetLong(json["timestampMs"], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        };
    }
}

public sealed class AndroidBluetoothPairedDevice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
