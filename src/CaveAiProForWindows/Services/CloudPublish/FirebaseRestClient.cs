using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using CaveAiProForWindows.Services.Auth;

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

    private static void EnsureNetworkAllowed() =>
        MicrosoftTestMode.ThrowIfNetworkBlocked("Firebase REST");

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
        EnsureNetworkAllowed();
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
        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

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

    /// <summary>Creates a document in a collection with a server-generated id.</summary>
    public async Task CreateDocumentAsync(
        FirebaseIdToken token,
        string collectionId,
        IReadOnlyDictionary<string, object> fields,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(collectionId))
            throw new ArgumentException("Collection id is required.", nameof(collectionId));
        if (fields.Count == 0)
            throw new ArgumentException("At least one field is required.", nameof(fields));

        var normalized = collectionId.Trim().Trim('/');
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{normalized}";

        var body = new FirestoreDocumentPatch { Fields = new Dictionary<string, object>(fields) };
        var json = JsonSerializer.Serialize(body, JsonOptions);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Firestore CREATE failed ({(int)resp.StatusCode}): {Truncate(respBody)}",
                resp.StatusCode,
                respBody);
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
        EnsureNetworkAllowed();
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

    /// <summary>Owner sync PATCH on <c>published_caves/{docId}</c> (publishedCaveOwnerSyncUpdateValid).</summary>
    public async Task PatchPublishedCaveAsync(
        FirebaseIdToken token,
        string publishedCaveDocId,
        PublishedCaveSyncPayload.OwnerSyncInput syncInput,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publishedCaveDocId))
            throw new ArgumentException("Published cave document id is required.", nameof(publishedCaveDocId));
        ArgumentNullException.ThrowIfNull(syncInput);

        var pairs = PublishedCaveSyncPayload.BuildOwnerSyncUpdatePairs(syncInput);
        var fields = FirestoreFieldBuilder.BuildFields(pairs);
        var docPath = $"published_caves/{publishedCaveDocId.Trim()}";
        await PatchDocumentAsync(token, docPath, fields, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads <c>published_caves/{docId}</c> for Public Library download (Firebase ID token required — rules allow signed-in read only).</summary>
    public async Task<PublishedCaveDocument> GetPublishedCaveDocumentAsync(
        string publishedDocId,
        FirebaseIdToken? token = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        if (string.IsNullOrWhiteSpace(publishedDocId))
            throw new ArgumentException("Published cave document id is required.", nameof(publishedDocId));

        var docPath = $"published_caves/{publishedDocId.Trim()}";
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{docPath}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Firestore GET failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);

        return ParsePublishedCaveDocument(body);
    }

    /// <summary>HEAD-equivalent via GET — returns whether a Firestore document exists.</summary>
    public async Task<bool> DocumentExistsAsync(
        FirebaseIdToken token,
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        var normalized = documentPath.Trim().TrimStart('/');
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{normalized}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        return resp.IsSuccessStatusCode;
    }

    public async Task DeleteDocumentAsync(
        FirebaseIdToken token,
        string documentPath,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        var normalized = documentPath.Trim().TrimStart('/');
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{normalized}";

        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new FirebaseRestException(
                $"Firestore DELETE failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);
        }
    }

    public async Task<IReadOnlyList<string>> ListSubcollectionDocumentIdsAsync(
        FirebaseIdToken token,
        string collectionPath,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        var normalized = collectionPath.Trim().TrimStart('/').TrimEnd('/');
        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents/{normalized}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Firestore LIST failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("documents", out var docs) || docs.ValueKind != JsonValueKind.Array)
            return [];

        var ids = new List<string>();
        foreach (var item in docs.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameEl))
                continue;
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            ids.Add(name.Split('/').LastOrDefault() ?? "");
        }

        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
    }

    /// <summary>Lists owner survey cloud projects ordered by <c>updatedAtMs</c> desc.</summary>
    public async Task<IReadOnlyList<Services.SurveyCloud.SurveyCloudProjectMeta>> QuerySurveyCloudProjectsAsync(
        FirebaseIdToken token,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        var uid = token.Subject?.Trim();
        if (string.IsNullOrWhiteSpace(uid))
            throw new InvalidOperationException("Firebase token missing uid (sub).");

        var url =
            $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_config.ProjectId)}/databases/{Uri.EscapeDataString(_config.FirestoreDatabaseId)}/documents:runQuery";

        var body = new Dictionary<string, object>
        {
            ["structuredQuery"] = new Dictionary<string, object>
            {
                ["from"] = new object[] { new Dictionary<string, object> { ["collectionId"] = "survey_projects" } },
                ["where"] = new Dictionary<string, object>
                {
                    ["fieldFilter"] = new Dictionary<string, object>
                    {
                        ["field"] = new Dictionary<string, object> { ["fieldPath"] = "ownerUid" },
                        ["op"] = "EQUAL",
                        ["value"] = new Dictionary<string, object> { ["stringValue"] = uid },
                    },
                },
                ["orderBy"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["field"] = new Dictionary<string, object> { ["fieldPath"] = "updatedAtMs" },
                        ["direction"] = "DESCENDING",
                    },
                },
                ["limit"] = 100,
            },
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new FirebaseRestException(
                $"Firestore runQuery failed ({(int)resp.StatusCode}): {Truncate(respBody)}",
                resp.StatusCode,
                respBody);

        using var doc = JsonDocument.Parse(respBody);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<Services.SurveyCloud.SurveyCloudProjectMeta>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            if (!row.TryGetProperty("document", out var document))
                continue;
            if (!document.TryGetProperty("fields", out var fields))
                continue;
            var projectId = ReadFirestoreString(fields, "projectId");
            if (string.IsNullOrWhiteSpace(projectId) && document.TryGetProperty("name", out var nameEl))
                projectId = nameEl.GetString()?.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(projectId))
                continue;
            var storagePath = ReadFirestoreString(fields, "projectJsonStoragePath")
                ?? $"survey_projects/{uid}/{projectId}/project.json";
            results.Add(new Services.SurveyCloud.SurveyCloudProjectMeta
            {
                ProjectId = projectId.Trim(),
                CaveName = ReadFirestoreString(fields, "caveName") ?? "Survey",
                UpdatedAtMs = ReadFirestoreLong(fields, "updatedAtMs"),
                ShotCount = (int)ReadFirestoreLong(fields, "shotCount"),
                ProjectJsonStoragePath = storagePath.Trim(),
                PlatformOrigin = ReadFirestoreString(fields, "platformOrigin") ?? "android",
            });
        }

        return results;
    }

    /// <summary>Downloads a Storage object using the Firebase ID token (owner rules).</summary>
    public async Task<byte[]> DownloadStorageObjectBytesAsync(
        FirebaseIdToken token,
        string storageObjectPath,
        CancellationToken cancellationToken = default)
    {
        EnsureNetworkAllowed();
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(storageObjectPath))
            throw new ArgumentException("Storage object path is required.", nameof(storageObjectPath));

        var normalizedPath = storageObjectPath.Trim().TrimStart('/');
        var nameParam = Uri.EscapeDataString(normalizedPath);
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(_config.StorageBucket)}/o/{nameParam}?alt=media";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Raw);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new FirebaseRestException(
                $"Storage download failed ({(int)resp.StatusCode}): {Truncate(body)}",
                resp.StatusCode,
                body);
        }

        return await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadFirestoreString(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var el) || !el.TryGetProperty("stringValue", out var s))
            return null;
        return s.GetString();
    }

    private static long ReadFirestoreLong(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var el))
            return 0L;
        if (el.TryGetProperty("integerValue", out var i) && long.TryParse(i.GetString(), out var parsed))
            return parsed;
        if (el.TryGetProperty("doubleValue", out var d))
            return (long)d.GetDouble();
        return 0L;
    }

    private static PublishedCaveDocument ParsePublishedCaveDocument(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var fields = doc.RootElement.GetProperty("fields");
        string? ReadString(string name)
        {
            if (!fields.TryGetProperty(name, out var el))
                return null;
            if (el.TryGetProperty("stringValue", out var s))
                return s.GetString();
            return null;
        }

        IReadOnlyList<string>? ReadStringArray(string name)
        {
            if (!fields.TryGetProperty(name, out var el) || !el.TryGetProperty("arrayValue", out var arr))
                return null;
            if (!arr.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<string>();
            foreach (var item in values.EnumerateArray())
            {
                if (item.TryGetProperty("stringValue", out var s))
                {
                    var v = s.GetString();
                    if (!string.IsNullOrWhiteSpace(v))
                        list.Add(v);
                }
            }

            return list.Count > 0 ? list : null;
        }

        double ReadDouble(string name)
        {
            if (!fields.TryGetProperty(name, out var el))
                return 0;
            if (el.TryGetProperty("doubleValue", out var d))
                return d.GetDouble();
            if (el.TryGetProperty("integerValue", out var i) && long.TryParse(i.GetString(), out var parsed))
                return parsed;
            return 0;
        }

        return new PublishedCaveDocument
        {
            DocumentId = doc.RootElement.TryGetProperty("name", out var n)
                ? n.GetString()?.Split('/').LastOrDefault() ?? ""
                : "",
            CaveName = ReadString("caveName"),
            Description = ReadString("description"),
            Depth = ReadDouble("depth"),
            Length = ReadDouble("length"),
            ImageUrls = ReadStringArray("imageUrls"),
            SurveyJsonUrl = ReadString("surveyJsonUrl"),
            SurveyJsonMediaUrl = ReadString("surveyJsonMediaUrl"),
            CartographyImageUrls = ReadStringArray("cartographyImageUrls"),
            SurfaceLidarUrl = ReadString("surfaceLidarUrl"),
            SurveyReportSummary = ReadString("surveyReportSummary"),
            SurveyReportNarrativeUrl = ReadString("surveyReportNarrativeUrl"),
            EntranceMagneticHintsJson = ReadString("entranceMagneticHintsJson"),
            EntranceMagneticHintsUrl = ReadString("entranceMagneticHintsUrl"),
            AccessSeasonNote = ReadString("accessSeasonNote"),
        };
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
