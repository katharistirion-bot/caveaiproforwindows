using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Builds Android-compatible <c>backup_manifest.json</c> and <c>integrity_manifest.json</c> for Windows ZIP export.</summary>
public static class BackupZipManifestWriter
{
    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static byte[] BuildBackupManifest(
        CaveProjectDocument project,
        bool includesMapInventory,
        bool bundlesLocalMedia,
        bool includesCaveLibrary = false,
        int knownCaveCount = 0)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        long? windowsEditedMs = null;
        if (project.ExtensionData != null &&
            project.ExtensionData.TryGetValue("windowsLastEditedAtMs", out var tsEl) &&
            tsEl.ValueKind == JsonValueKind.Number &&
            tsEl.TryGetInt64(out var ms))
        {
            windowsEditedMs = ms;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("backup_version", 2);
            writer.WriteString("created_at", now);
            writer.WriteString("mode", "windows_pc_export");
            writer.WriteNumber("project_count", 1);
            writer.WriteString("app", "CAVE AI PRO Windows");
            writer.WriteString("app_version", AppMetadata.InformationalVersion);
            writer.WriteBoolean("bundles_local_media", bundlesLocalMedia);
            writer.WriteBoolean("includes_map_inventory", includesMapInventory);
            writer.WriteBoolean("includes_cave_library", includesCaveLibrary);
            writer.WriteNumber("known_cave_count", knownCaveCount);
            writer.WriteString("target_app", "CaveAI Pro Android");
            writer.WriteString("handoff_kind", "single_project_round_trip");

            writer.WriteStartObject("edit_provenance");
            writer.WriteString("editor", "CAVE AI PRO Windows");
            writer.WriteString("editor_version", AppMetadata.InformationalVersion);
            if (windowsEditedMs is { } edited)
                writer.WriteNumber("windowsLastEditedAtMs", edited);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] BuildIntegrityManifest(IReadOnlyDictionary<string, string> fileHashes)
    {
        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithm", "SHA-256");
            writer.WriteString("generated_at", generatedAt);
            writer.WriteStartObject("files");
            foreach (var (path, hex) in fileHashes.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
                writer.WriteString(path, hex);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
