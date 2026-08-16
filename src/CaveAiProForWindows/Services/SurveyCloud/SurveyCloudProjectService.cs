using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using SurveyZip = CaveAiProForWindows.Services.SurveyPortableZipExporter;

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

    private static readonly Regex ProjectIdRegex = new("^[a-zA-Z0-9_-]{8,128}$", RegexOptions.CultureInvariant);

    public static string EnsureProjectId(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var id = project.ProjectId?.Trim();
        if (!string.IsNullOrWhiteSpace(id) && ProjectIdRegex.IsMatch(id))
            return id;
        id = Guid.NewGuid().ToString("N");
        project.ProjectId = id;
        return id;
    }

    /// <summary>
    /// Upload selected project JSON to Storage <c>survey_projects/{uid}/{projectId}/project.json</c>
    /// and upsert Firestore <c>survey_projects/{projectId}</c> (Android contract, <c>platformOrigin=windows</c>).
    /// </summary>
    public static async Task<SurveyCloudProjectMeta> UploadProjectAsync(
        FirebaseIdToken token,
        CaveProjectDocument project,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(project);
        var uid = token.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Sign-in token is missing the user id. Sign in again and retry.");

        var projectId = EnsureProjectId(project);
        project.SurveyArchivedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (string.IsNullOrWhiteSpace(project.SurveyArchiveSchemaVersion))
            project.SurveyArchiveSchemaVersion = "1";

        var json = SurveyZip.SerializeSingleProjectObject(project);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > 35 * 1024 * 1024)
            throw new InvalidOperationException("Project JSON exceeds cloud size limit (35 MB).");

        var storagePath = $"survey_projects/{uid}/{projectId}/project.json";
        using var rest = new FirebaseRestClient();
        await rest.UploadBytesAsync(token, storagePath, bytes, "application/json", cancellationToken)
            .ConfigureAwait(false);

        var caveName = string.IsNullOrWhiteSpace(project.Name) ? "Untitled survey" : project.Name.Trim();
        if (caveName.Length > 500)
            caveName = caveName[..500];

        var pairs = new List<KeyValuePair<string, object?>>
        {
            new("ownerUid", uid),
            new("projectId", projectId),
            new("version", 1L),
            new("caveName", caveName),
            new("updatedAtMs", project.SurveyArchivedAtMs.Value),
            new("shotCount", (long)project.Shots.Count),
            new("projectJsonStoragePath", storagePath),
            new("platformOrigin", "windows"),
        };
        if (project.Lat is { } lat && project.Lon is { } lon &&
            lat is >= -90 and <= 90 && lon is >= -180 and <= 180 &&
            !(Math.Abs(lat) < 1e-9 && Math.Abs(lon) < 1e-9))
        {
            pairs.Add(new("entranceLat", lat));
            pairs.Add(new("entranceLon", lon));
            pairs.Add(new("entranceAltM", project.Alt));
        }

        var fields = FirestoreFieldBuilder.BuildFields(pairs);
        var existing = await rest.GetDocumentJsonAsync(token, $"survey_projects/{projectId}", cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(existing))
        {
            await rest.CreateDocumentWithIdAsync(token, "survey_projects", projectId, fields, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await rest.PatchDocumentAsync(token, $"survey_projects/{projectId}", fields, cancellationToken)
                .ConfigureAwait(false);
        }

        return new SurveyCloudProjectMeta
        {
            ProjectId = projectId,
            CaveName = caveName,
            UpdatedAtMs = project.SurveyArchivedAtMs.Value,
            ShotCount = project.Shots.Count,
            ProjectJsonStoragePath = storagePath,
            PlatformOrigin = "windows",
        };
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