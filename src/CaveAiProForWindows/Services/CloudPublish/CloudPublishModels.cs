namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Result of a Firebase Storage simple media upload.</summary>
public sealed class FirebaseStorageUploadResult
{
    public required string ObjectName { get; init; }

    public required string Bucket { get; init; }

    public string? DownloadToken { get; init; }

    /// <summary>HTTPS URL suitable for storing on <c>published_caves</c> (includes download token when present).</summary>
    public required string MediaUrl { get; init; }
}

/// <summary>Metadata written to an existing <c>published_caves/{docId}</c> document.</summary>
public sealed class CloudPublishMetadata
{
    public required string PublishedCaveDocId { get; init; }

    /// <summary>HTTPS cartography image URLs for Android Public Library (<c>cartographyImageUrls</c>).</summary>
    public IReadOnlyList<string>? CartographyImageUrls { get; init; }

    [Obsolete("Use CartographyImageUrls — Android reads cartographyImageUrls (array).")]
    public string? CartographyImageUrl
    {
        get => CartographyImageUrls is { Count: > 0 } urls ? urls[0] : null;
        init => CartographyImageUrls = string.IsNullOrWhiteSpace(value) ? null : new[] { value };
    }

    public string? StructureMaskUrl { get; init; }

    public string? SurveyJsonStoragePath { get; init; }

    public string? SurveyJsonMediaUrl { get; init; }

    /// <summary>Human-readable overlay summary (distances, pins, loops) for website metadata.</summary>
    public string? SurveyOverlaySummary { get; init; }

    public string? SurveyArchiveSchemaVersion { get; init; }

    /// <summary>Optional reference catalog pin id (cross-platform publish parity).</summary>
    public string? ReferenceCatalogId { get; init; }

    /// <summary>Optional reference catalog country hint.</summary>
    public string? ReferenceCatalogCountry { get; init; }

    /// <summary>Normalized prefix search key (Firestore <c>caveNameSearchKey</c>).</summary>
    public string? CaveNameSearchKey { get; init; }

    public string SourceClient { get; init; } = "windows";

    public long UpdatedAtUtcMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>Subset of a Firestore <c>published_caves</c> document for library download.</summary>
public sealed class PublishedCaveDocument
{
    public string DocumentId { get; init; } = "";

    public string? CaveName { get; init; }

    public string? SurveyJsonUrl { get; init; }

    public string? SurveyJsonMediaUrl { get; init; }

    public IReadOnlyList<string>? CartographyImageUrls { get; init; }
}
