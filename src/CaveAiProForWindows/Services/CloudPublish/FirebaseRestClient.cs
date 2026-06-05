using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Lightweight Firebase Storage + Firestore REST client using <see cref="HttpClient"/> and a sniffed ID token.
/// No Firebase C# SDK.
/// </summary>
public sealed class FirebaseRestClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly FirebaseProjectConfig _config;

    public FirebaseRestClient(FirebaseProjectConfig? config = null, HttpMessageHandler? handler = null)
    {
        _config = config ?? FirebaseProjectConfig.LoadFromEnvironment();
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// Simple media upload to Firebase Storage.
    /// POST https://firebasestorage.googleapis.com/v0/b/{bucket}/o?uploadType=media&amp;name={path}
    /// </summary>
    public async Task<FirebaseStorageUploadResult> UploadBytesAsync(
        FirebaseIdToken token,
        string storageObjectPath,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(storageObjectPath))
            throw new ArgumentException("Storage object path is required.", nameof(storageObjectPath));

        var normalizedPath = storageObjectPath.Trim().TrimStart('/');
        var nameParam = Uri.EscapeDataString(normalizedPath);
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(_config.StorageBucket)}/o?uploadType=media&name={nameParam}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        req.Content = new ByteArrayContent(content);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Storage upload failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var objectName = root.GetProperty("name").GetString()
            ?? throw new FirebaseRestException("Storage response missing name.", resp.StatusCode, body);
        var bucket = root.TryGetProperty("bucket", out var bEl) ? bEl.GetString() ?? _config.StorageBucket : _config.StorageBucket;
        string? downloadToken = null;
        if (root.TryGetProperty("downloadTokens", out var tokEl))
            downloadToken = tokEl.GetString();

        var mediaUrl = BuildStorageMediaUrl(bucket, objectName, downloadToken);
        return new FirebaseStorageUploadResult
        {
            ObjectName = objectName,
            Bucket = bucket,
            DownloadToken = downloadToken,
            MediaUrl = mediaUrl,
        };
    }

    /// <summary>
    /// PATCH an existing Firestore document (partial update with field mask).
    /// </summary>
    public async Task PatchDocumentAsync(
        FirebaseIdToken token,
        string documentPath,
        IReadOnlyDictionary<string, object> fields,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(documentPath))
            throw new ArgumentException("Document path is required.", nameof(documentPath));
        if (fields.Count == 0)
            return;

        var normalized = documentPath.Trim().TrimStart('/');
        var updateMask = string.Join(",", fields.Keys.Select(Uri.EscapeDataString));
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{normalized}?updateMask.fieldPaths={updateMask}";

        var patchBody = new FirestoreDocumentPatch { Fields = new Dictionary<string, object>(fields) };
        var json = JsonSerializer.Serialize(patchBody, JsonOptions);

        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Firestore PATCH failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);
    }

    /// <summary>Updates cartography / survey metadata on <c>published_caves/{docId}</c>.</summary>
    public async Task PatchPublishedCaveAsync(
        FirebaseIdToken token,
        CloudPublishMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var pairs = new List<KeyValuePair<string, object?>>
        {
            new("updatedAtMs", metadata.UpdatedAtUtcMs),
            new("sourceClient", metadata.SourceClient),
        };

        if (!string.IsNullOrWhiteSpace(metadata.CartographyImageUrl))
            pairs.Add(new("cartographyImageUrl", metadata.CartographyImageUrl));
        if (!string.IsNullOrWhiteSpace(metadata.StructureMaskUrl))
            pairs.Add(new("structureMaskUrl", metadata.StructureMaskUrl));
        if (!string.IsNullOrWhiteSpace(metadata.SurveyJsonStoragePath))
            pairs.Add(new("surveyJsonStoragePath", metadata.SurveyJsonStoragePath));
        if (!string.IsNullOrWhiteSpace(metadata.SurveyJsonMediaUrl))
            pairs.Add(new("surveyJsonMediaUrl", metadata.SurveyJsonMediaUrl));
        if (!string.IsNullOrWhiteSpace(metadata.SurveyArchiveSchemaVersion))
            pairs.Add(new("surveyArchiveSchemaVersion", metadata.SurveyArchiveSchemaVersion));

        var fields = FirestoreFieldBuilder.BuildFields(pairs);
        var docPath = $"published_caves/{metadata.PublishedCaveDocId.Trim()}";
        await PatchDocumentAsync(token, docPath, fields, cancellationToken).ConfigureAwait(false);
    }

    public static string BuildStorageMediaUrl(string bucket, string objectName, string? downloadToken)
    {
        var encoded = Uri.EscapeDataString(objectName);
        var baseUrl =
            $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o/{encoded}?alt=media";
        return string.IsNullOrWhiteSpace(downloadToken)
            ? baseUrl
            : $"{baseUrl}&token={Uri.EscapeDataString(downloadToken)}";
    }

    private static string Truncate(string? s, int max = 600) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    public void Dispose() => _http.Dispose();
}

/// <summary>Firebase REST call failure with HTTP context.</summary>
public sealed class FirebaseRestException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    public FirebaseRestException(string message, HttpStatusCode statusCode, string? responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
