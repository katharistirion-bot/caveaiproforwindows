using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a portable <c>.zip</c> from the <b>currently selected</b> cave: Android-compatible <c>data.json</c>
/// (single-project array) plus a <b>PNG snapshot</b> of the Plan canvas (what the user sees, including zoom/pan).
/// Input data on disk is typically CaveAI Pro <b>JSON</b> or backup <b>ZIP</b> from Android; this exporter serializes the in-memory <see cref="CaveProjectDocument"/>.
/// </summary>
public static class SurveyPortableZipExporter
{
    private static readonly JsonSerializerOptions WriteJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    /// <summary>Writes UTF-8 BOM <c>data.json</c> (one project), <c>plan_view.png</c>, and <c>README.txt</c>.</summary>
    public static void WriteZip(CaveProjectDocument project, ReadOnlySpan<byte> planPngBytes, string zipPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(zipPath))
            throw new ArgumentException("ZIP path is required.", nameof(zipPath));

        var jsonText = SerializeSingleProjectArray(project);
        var jsonBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(jsonText);

        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        AddBytes(zip, "data.json", jsonBytes);
        AddBytes(zip, "plan_view.png", planPngBytes.ToArray());

        var readme =
            "CAVE AI PRO — Windows portable export\r\n" +
            "- data.json: one CaveProject in a JSON array (same field names as Android Gson backup).\r\n" +
            "- plan_view.png: raster snapshot of the Plan tab canvas (current zoom/pan).\r\n";
        AddBytes(zip, "README.txt", Encoding.UTF8.GetBytes(readme));
    }

    private static void AddBytes(ZipArchive zip, string entryName, byte[] bytes)
    {
        var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Same top-level shape as CaveAI backup: <c>[ { ... } ]</c>.</summary>
    public static string SerializeSingleProjectArray(CaveProjectDocument project) =>
        SerializeProjectArray(new[] { project });

    /// <summary>Single project object (Android Gson <c>project.json</c> / Survey Cloud Storage).</summary>
    public static string SerializeSingleProjectObject(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        CaveProjectJsonWriteNormalizer.Prepare(project);
        return JsonSerializer.Serialize(project, WriteJson);
    }

    /// <summary>Serializes one or more projects as a Gson-compatible JSON array.</summary>
    public static string SerializeProjectArray(IReadOnlyList<CaveProjectDocument> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        CaveProjectJsonWriteNormalizer.PrepareAll(projects);
        return JsonSerializer.Serialize(projects, WriteJson);
    }
}
