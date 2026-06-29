using System.IO;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Services.CloudPublish;

namespace CaveAiProForWindows.Services.SurveyCloud;

public static class SurveyCloudProjectService
{
    public static async Task<IReadOnlyList<SurveyCloudProjectMeta>> ListOwnerProjectsAsync(
        FirebaseIdToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        using var rest = new FirebaseRestClient();
        return await rest.QuerySurveyCloudProjectsAsync(token, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> DownloadProjectJsonBytesAsync(
        FirebaseIdToken token,
        SurveyCloudProjectMeta meta,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(meta);
        if (string.IsNullOrWhiteSpace(meta.ProjectJsonStoragePath))
            throw new InvalidOperationException("Missing projectJsonStoragePath on cloud manifest.");

        using var rest = new FirebaseRestClient();
        return await rest.DownloadStorageObjectBytesAsync(token, meta.ProjectJsonStoragePath, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<string> DownloadProjectJsonToTempFileAsync(
        FirebaseIdToken token,
        SurveyCloudProjectMeta meta,
        CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadProjectJsonBytesAsync(token, meta, cancellationToken).ConfigureAwait(false);
        var dir = Path.Combine(Path.GetTempPath(), "CaveAiProForWindows", "SurveyCloud");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"survey_cloud_{meta.ProjectId}.json");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public static string NormalizeProjectJsonForImport(byte[] jsonBytes)
    {
        var text = Encoding.UTF8.GetString(jsonBytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        if (text.StartsWith("{", StringComparison.Ordinal))
            return $"[{text}]";
        return text;
    }

    /// <summary>Download surfaceLidarRaster sibling from Survey Cloud Storage into cacheDir.</summary>
    public static async Task<string?> TryDownloadSurfaceLidarRasterAsync(
        FirebaseIdToken token,
        SurveyCloudProjectMeta meta,
        string imageUriRelative,
        string cacheDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(meta);
        var rel = imageUriRelative.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(rel) ||
            rel.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            rel.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        var basePath = meta.ProjectJsonStoragePath.Replace("/project.json", "", StringComparison.Ordinal);
        var storagePath = $"{basePath}/{rel}";
        Directory.CreateDirectory(cacheDir);
        var localPath = Path.Combine(cacheDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        using var rest = new FirebaseRestClient();
        var bytes = await rest.DownloadStorageObjectBytesAsync(token, storagePath, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(localPath, bytes, cancellationToken).ConfigureAwait(false);
        return localPath;
    }

    /// <summary>Download <c>surfaceLidarRaster</c> sibling when <paramref name="projectJson"/> contains a relative <c>imageUri</c>.</summary>
    public static async Task TryDownloadSurfaceLidarFromProjectJsonAsync(
        FirebaseIdToken token,
        SurveyCloudProjectMeta meta,
        byte[] projectJson,
        string cacheDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(meta);
        if (projectJson.Length == 0)
            return;

        using var doc = JsonDocument.Parse(projectJson);
        var root = doc.RootElement;
        var project = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
            ? root[0]
            : root;
        if (!project.TryGetProperty("surfaceLidarRaster", out var lidar) ||
            lidar.ValueKind != JsonValueKind.Object)
            return;
        if (!lidar.TryGetProperty("imageUri", out var uriEl) ||
            uriEl.ValueKind != JsonValueKind.String)
            return;

        var imageUri = uriEl.GetString();
        if (string.IsNullOrWhiteSpace(imageUri))
            return;

        await TryDownloadSurfaceLidarRasterAsync(token, meta, imageUri, cacheDir, cancellationToken)
            .ConfigureAwait(false);
    }
}