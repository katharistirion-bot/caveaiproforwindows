using System.IO;
using System.Text.Json;

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

    /// <summary>Web API key for bundled desktop auth fallback (env: CAVEAIPRO_FIREBASE_API_KEY).</summary>
    public string? WebApiKey { get; init; }

    public static FirebaseProjectConfig LoadFromEnvironment()
    {
        var projectId = Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_PROJECT_ID");
        var bucket = Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_STORAGE_BUCKET");
        var apiKey = Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_API_KEY");
        var embeddedKey = TryLoadEmbeddedWebApiKey();
        return new FirebaseProjectConfig
        {
            ProjectId = string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim(),
            StorageBucket = string.IsNullOrWhiteSpace(bucket)
                ? $"{(string.IsNullOrWhiteSpace(projectId) ? DefaultProjectId : projectId.Trim())}.appspot.com"
                : bucket.Trim(),
            WebApiKey = !string.IsNullOrWhiteSpace(apiKey)
                ? apiKey.Trim()
                : embeddedKey,
        };
    }

    private static string? TryLoadEmbeddedWebApiKey()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopAuth", "firebase-config.json");
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("apiKey", out var keyEl))
                return null;
            var key = keyEl.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch
        {
            return null;
        }
    }
}
