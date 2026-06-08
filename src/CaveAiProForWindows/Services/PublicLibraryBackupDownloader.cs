using System.IO;
using System.Net.Http;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
namespace CaveAiProForWindows.Services;

/// <summary>Downloads a published cave's survey JSON (+ cartography assets) into a local backup ZIP or JSON.</summary>
public static class PublicLibraryBackupDownloader
{
    public sealed class DownloadResult
    {
        public required string OutputPath { get; init; }

        public int AssetCount { get; init; }

        public string? CaveName { get; init; }
    }

    public static async Task<DownloadResult> DownloadAsync(
        string publishedDocId,
        string outputPath,
        FirebaseIdToken? token = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publishedDocId))
            throw new ArgumentException("Published cave document id is required.", nameof(publishedDocId));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is required.", nameof(outputPath));

        using var rest = new FirebaseRestClient();
        progress?.Report("Fetching published cave metadata…");
        var doc = await rest.GetPublishedCaveDocumentAsync(publishedDocId.Trim(), token, cancellationToken)
            .ConfigureAwait(false);

        var surveyUrl = doc.SurveyJsonUrl ?? doc.SurveyJsonMediaUrl;
        if (string.IsNullOrWhiteSpace(surveyUrl))
            throw new InvalidOperationException("This published cave has no surveyJsonUrl — nothing to download.");

        progress?.Report("Downloading survey JSON…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var jsonBytes = await http.GetByteArrayAsync(surveyUrl, cancellationToken).ConfigureAwait(false);

        var projects = ExplorationDataLoader.DeserializeProjectsFromText(Encoding.UTF8.GetString(jsonBytes));
        var project = projects.FirstOrDefault()
            ?? throw new InvalidOperationException("Survey JSON did not contain a valid project.");

        var assetCount = 0;
        var ext = Path.GetExtension(outputPath);
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("Building backup ZIP…");
            assetCount = await WriteZipAsync(outputPath, project, jsonBytes, doc.CartographyImageUrls, http, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            progress?.Report("Writing JSON backup…");
            File.WriteAllBytes(outputPath, jsonBytes);
        }

        return new DownloadResult
        {
            OutputPath = outputPath,
            AssetCount = assetCount,
            CaveName = project.Name,
        };
    }

    private static async Task<int> WriteZipAsync(
        string zipPath,
        CaveProjectDocument project,
        byte[] jsonBytes,
        IReadOnlyList<string>? cartographyUrls,
        HttpClient http,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var temp = zipPath + ".tmp";
        if (File.Exists(temp))
            File.Delete(temp);

        var assetCount = 0;
        using (var fs = File.Create(temp))
        using (var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            WriteEntry(zip, "data.json", jsonBytes);

            if (cartographyUrls is { Count: > 0 })
            {
                for (var i = 0; i < cartographyUrls.Count; i++)
                {
                    var url = cartographyUrls[i];
                    if (string.IsNullOrWhiteSpace(url))
                        continue;
                    progress?.Report($"Downloading cartography asset {i + 1}/{cartographyUrls.Count}…");
                    try
                    {
                        var bytes = await http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                        var caveFolder = SanitizeFolderName(project.Name);
                        var entry = $"export_assets/library/{caveFolder}/cartography_{i + 1}.png";
                        WriteEntry(zip, entry, bytes);
                        assetCount++;
                    }
                    catch
                    {
                        /* skip failed asset */
                    }
                }
            }

            var readme = new UTF8Encoding(false).GetBytes(
                "CAVE AI PRO — Public Library download\r\n" +
                "- data.json: survey project from published_caves Firestore document.\r\n" +
                "- export_assets/library/: cartography images when available.\r\n");
            WriteEntry(zip, "README.txt", readme);
        }

        File.Move(temp, zipPath, overwrite: true);
        return assetCount;
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static string SanitizeFolderName(string? name)
    {
        var s = string.Join("_", (name ?? "cave").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrEmpty(s) ? "cave" : s;
    }
}
