using System.IO;
using System.Security.Cryptography;
using System.Text;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Best-effort anonymised crash / error telemetry. Never includes survey file paths or project content.
/// Queues locally when offline; uploads to Firestore when the user is signed in.
/// </summary>
public static class ClientErrorTelemetryService
{
    private const string CollectionId = "desktop_client_telemetry";
    private const int MaxStackPreviewChars = 4000;
    private const int MaxQueuedLines = 40;
    private static readonly object Gate = new();
    private static bool _uploadInFlight;

    private static string QueuePath =>
        Path.Combine(DiagnosticLogPaths.AppDataDirectory, "telemetry-queue.jsonl");

    public static void Report(string kind, Exception exception, string? context = null)
    {
        if (exception == null)
            return;

        try
        {
            var payload = BuildPayload(kind, exception, context);
            AppendLocalQueue(payload);
            _ = TryUploadQueuedAsync();
        }
        catch
        {
            /* telemetry must never crash the app */
        }
    }

    internal static TelemetryPayload BuildPayload(string kind, Exception exception, string? context)
    {
        var message = exception.Message ?? exception.GetType().Name;
        var stack = exception.StackTrace ?? "";
        if (string.IsNullOrWhiteSpace(stack))
            stack = exception.GetType().Name + ": " + message;
        if (exception.InnerException != null)
        {
            stack += Environment.NewLine + "Inner: " + exception.InnerException.GetType().Name + ": " +
                     exception.InnerException.Message;
        }

        return BuildPayload(kind, message, context, stack);
    }

    internal static TelemetryPayload BuildPayload(string kind, string message, string? context, string? stack = null)
    {
        var preview = RedactAndTruncate(string.IsNullOrWhiteSpace(stack) ? message : stack!, MaxStackPreviewChars);
        var hash = Sha256Hex((kind + "|" + message + "|" + preview).ToLowerInvariant());
        return new TelemetryPayload(
            kind.Trim(),
            hash,
            preview,
            context?.Trim(),
            AppMetadata.InformationalVersion,
            Environment.OSVersion.VersionString,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static async Task TryUploadQueuedAsync()
    {
        lock (Gate)
        {
            if (_uploadInFlight)
                return;
            _uploadInFlight = true;
        }

        try
        {
            var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
            if (token == null)
                return;

            var lines = ReadQueueLines();
            if (lines.Count == 0)
                return;

            var rest = new FirebaseRestClient();
            var remaining = new List<string>(lines.Count);

            foreach (var line in lines)
            {
                if (!TelemetryPayload.TryParse(line, out var payload) || payload == null)
                    continue;

                try
                {
                    await rest.CreateDocumentAsync(token, CollectionId, payload.ToFirestoreFields())
                        .ConfigureAwait(false);
                }
                catch
                {
                    remaining.Add(line);
                }
            }

            WriteQueueLines(remaining);
        }
        finally
        {
            lock (Gate)
                _uploadInFlight = false;
        }
    }

    private static void AppendLocalQueue(TelemetryPayload payload)
    {
        Directory.CreateDirectory(DiagnosticLogPaths.AppDataDirectory);
        var lines = ReadQueueLines();
        lines.Add(payload.Serialize());
        while (lines.Count > MaxQueuedLines)
            lines.RemoveAt(0);
        WriteQueueLines(lines);
    }

    private static List<string> ReadQueueLines()
    {
        if (!File.Exists(QueuePath))
            return new List<string>();

        return File.ReadAllLines(QueuePath, Encoding.UTF8)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
    }

    private static void WriteQueueLines(IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(DiagnosticLogPaths.AppDataDirectory);
        if (lines.Count == 0)
        {
            try
            {
                if (File.Exists(QueuePath))
                    File.Delete(QueuePath);
            }
            catch
            {
                /* ignore */
            }

            return;
        }

        File.WriteAllText(QueuePath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
    }

    private static string RedactAndTruncate(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var redacted = DiagnosticLogRedactor.RedactLine(text);
        if (redacted.Length <= maxChars)
            return redacted;
        return redacted[..maxChars] + "...";
    }

    private static string Sha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal sealed record TelemetryPayload(
        string Kind,
        string MessageHash,
        string StackPreview,
        string? Context,
        string AppVersion,
        string OsVersion,
        long CreatedAtMs)
    {
        public string Serialize() =>
            string.Join('\t',
                Kind,
                MessageHash,
                CreatedAtMs,
                AppVersion,
                OsVersion,
                Context ?? "",
                StackPreview.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', '|'));

        public static bool TryParse(string line, out TelemetryPayload? payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var parts = line.Split('\t');
            if (parts.Length < 7 || !long.TryParse(parts[2], out var createdAtMs))
                return false;

            payload = new TelemetryPayload(
                parts[0],
                parts[1],
                parts[6].Replace('|', '\n'),
                string.IsNullOrEmpty(parts[5]) ? null : parts[5],
                parts[3],
                parts[4],
                createdAtMs);
            return true;
        }

        public IReadOnlyDictionary<string, object> ToFirestoreFields() =>
            FirestoreFieldBuilder.BuildFields([
                new KeyValuePair<string, object?>("platform", "windows"),
                new KeyValuePair<string, object?>("kind", Kind),
                new KeyValuePair<string, object?>("messageHash", MessageHash),
                new KeyValuePair<string, object?>("stackPreview", StackPreview),
                new KeyValuePair<string, object?>("context", Context ?? ""),
                new KeyValuePair<string, object?>("appVersion", AppVersion),
                new KeyValuePair<string, object?>("osVersion", OsVersion),
                new KeyValuePair<string, object?>("createdAtMs", CreatedAtMs),
            ]);
    }
}