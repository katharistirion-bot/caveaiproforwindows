namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>
/// Public Firebase project identifiers (same values shipped in the web/Android client config).
/// Override via environment variables for staging without changing the website.
/// </summary>
public sealed class FirebaseProjectConfig
{
    /// <summary>Default matches <see cref="PublicLibraryCatalog.FirebaseHostingOrigin"/> project id.</summary>
    public const string DefaultProjectId = "caveaipro-5950e";

    public string ProjectId { get; init; } = DefaultProjectId;

    /// <summary>GCS bucket backing Firebase Storage (classic: {projectId}.appspot.com).</summary>
    public string StorageBucket { get; init; } = $"{DefaultProjectId}.appspot.com";

    public string FirestoreDatabaseId { get; init; } = "(default)";

    public static FirebaseProjectConfig LoadFromEnvironment()
    {
        var projectId = Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_PROJECT_ID");
        var bucket = Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_STORAGE_BUCKET");
        return new FirebaseProjectConfig
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim(),
            StorageBucket = string.IsNullOrWhiteSpace(bucket)
                ? $"{(string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim())}.appspot.com"
                : bucket.Trim(),
        };
    }
}
