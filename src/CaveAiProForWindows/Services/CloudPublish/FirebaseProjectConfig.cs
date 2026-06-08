using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Firebase web client config (public identifiers + API key) for Auth in WebView2.</summary>
public sealed class FirebaseWebClientConfig
{
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = "";

    [JsonPropertyName("authDomain")]
    public string AuthDomain { get; init; } = "";

    [JsonPropertyName("projectId")]
    public string ProjectId { get; init; } = FirebaseProjectConfig.DefaultProjectId;

    [JsonPropertyName("storageBucket")]
    public string StorageBucket { get; init; } = $"{FirebaseProjectConfig.DefaultProjectId}.appspot.com";

    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Shape expected by <c>firebase.initializeApp</c> in bundled auth.html.</summary>
    public object ToFirebaseInitializeAppObject() => new
    {
        apiKey = ApiKey,
        authDomain = string.IsNullOrWhiteSpace(AuthDomain)
            ? $"{ProjectId}.firebaseapp.com"
            : AuthDomain,
        projectId = ProjectId,
        storageBucket = StorageBucket,
    };
}

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

    public string AuthDomain { get; init; } = $"{DefaultProjectId}.firebaseapp.com";

    public static FirebaseProjectConfig LoadFromEnvironment()
    {
        var embedded = TryLoadEmbeddedWebClientConfig();
        var projectId = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_PROJECT_ID"),
            embedded?.ProjectId,
            DefaultProjectId)!;
        var bucket = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_STORAGE_BUCKET"),
            embedded?.StorageBucket,
            $"{projectId}.appspot.com")!;
        var apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_API_KEY"),
            embedded?.ApiKey);
        var authDomain = FirstNonEmpty(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_AUTH_DOMAIN"),
            embedded?.AuthDomain,
            $"{projectId}.firebaseapp.com")!;

        return new FirebaseProjectConfig
        {
            ProjectId = projectId,
            StorageBucket = bucket,
            WebApiKey = apiKey,
            AuthDomain = authDomain,
        };
    }

    public FirebaseWebClientConfig ToWebClientConfig() =>
        new()
        {
            ApiKey = WebApiKey ?? "",
            AuthDomain = AuthDomain,
            ProjectId = ProjectId,
            StorageBucket = StorageBucket,
        };

    private static FirebaseWebClientConfig? TryLoadEmbeddedWebClientConfig()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopAuth", "firebase-config.json");
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<FirebaseWebClientConfig>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }
}
