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

    public string? CartographyImageUrl { get; init; }

    public string? StructureMaskUrl { get; init; }

    public string? SurveyJsonStoragePath { get; init; }

    public string? SurveyJsonMediaUrl { get; init; }

    /// <summary>Human-readable overlay summary (distances, pins, loops) for website metadata.</summary>
    public string? SurveyOverlaySummary { get; init; }

    public string? SurveyArchiveSchemaVersion { get; init; }

    public string SourceClient { get; init; } = "windows";

    public long UpdatedAtUtcMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
