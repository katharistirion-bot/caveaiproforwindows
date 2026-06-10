using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Reads <c>backup_manifest.json</c> from a CaveAI Pro backup ZIP (same layout as Android <c>ProjectBackupZip</c>).</summary>
public static class BackupManifestReader
{
    public static string TryReadSummary(string zipPath)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("backup_manifest.json");
            if (entry == null)
                return "No backup_manifest.json (older export or non–CaveAI ZIP).";

            using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
            var text = sr.ReadToEnd();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.AppendLine("backup_manifest.json");
            sb.AppendLine(new string('-', 40));
            void Line(string key)
            {
                if (!root.TryGetProperty(key, out var el)) return;
                var v = el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString() ?? "",
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => el.GetRawText(),
                };
                sb.AppendLine($"{key}: {v}");
            }

            Line("backup_version");
            Line("created_at");
            Line("mode");
            Line("project_count");
            Line("app");
            Line("app_version");
            Line("bundles_local_media");
            Line("includes_map_inventory");
            if (root.TryGetProperty("edit_provenance", out var prov) && prov.ValueKind == JsonValueKind.Object)
            {
                if (prov.TryGetProperty("editor", out var ed) && ed.ValueKind == JsonValueKind.String)
                    sb.AppendLine($"edit_provenance.editor: {ed.GetString()}");
                if (prov.TryGetProperty("editor_version", out var ev) && ev.ValueKind == JsonValueKind.String)
                    sb.AppendLine($"edit_provenance.editor_version: {ev.GetString()}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return "Could not read backup_manifest.json: " + ex.Message;
        }
    }

    /// <summary>Android <c>app_version</c> from manifest, if present.</summary>
    public static string? TryReadAndroidAppVersion(string zipPath)
    {
        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("backup_manifest.json");
            if (entry == null)
                return null;
            using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
            using var doc = JsonDocument.Parse(sr.ReadToEnd());
            if (doc.RootElement.TryGetProperty("app_version", out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString();
            return null;
        }
        catch
        {
            return null;
        }
    }
}
