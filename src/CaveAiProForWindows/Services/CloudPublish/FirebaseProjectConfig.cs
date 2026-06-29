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
    public string StorageBucket { get; init; } = $"{FirebaseProjectConfig.DefaultProjectId}.firebasestorage.app";

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

    /// <summary>Firebase Storage bucket ({projectId}.firebasestorage.app).</summary>
    public string StorageBucket { get; init; } = $"{DefaultProjectId}.firebasestorage.app";

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
            $"{projectId}.firebasestorage.app")!;
        var apiKey = ResolveWebApiKey(
            Environment.GetEnvironmentVariable("CAVEAIPRO_FIREBASE_API_KEY"),
            embedded?.ApiKey,
            ObservedWebApiKey);
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

    /// <summary>
    /// Last Firebase Web API key seen on an OAuth handler URL in WebView2 (same public key as the website SDK).
    /// </summary>
    internal static string? ObservedWebApiKey { get; private set; }

    /// <summary>Records <c>apiKey=</c> from Firebase auth handler navigations when config is not injected yet.</summary>
    internal static void TryObserveWebApiKeyFromUri(string? uriString)
    {
        if (string.IsNullOrWhiteSpace(uriString))
            return;
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            return;

        if (!TryReadQueryParam(uri.Query, "apiKey", out var key)
            && !TryReadFragmentParam(uri.Fragment, "apiKey", out key))
            return;

        SetObservedWebApiKey(key);
    }

    internal static void SetObservedWebApiKey(string? key)
    {
        if (!IsUsableApiKey(key))
            return;

        ObservedWebApiKey = key!.Trim();
    }

    /// <summary>Env → embedded config → WebView-observed key (skips REPLACE_AT_BUILD placeholders).</summary>
    internal static string? ResolveWebApiKey(params string?[] candidates)
    {
        foreach (var v in candidates)
        {
            if (IsUsableApiKey(v))
                return v!.Trim();
        }

        return null;
    }

    internal static bool IsUsableApiKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static bool TryReadQueryParam(string query, string name, out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(query))
            return false;

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            if (!string.Equals(part[..eq], name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = Uri.UnescapeDataString(part[(eq + 1)..]);
            return true;
        }

        return false;
    }

    private static bool TryReadFragmentParam(string fragment, string name, out string? value)
    {
        value = null;
        if (fragment.Length <= 1)
            return false;

        var trimmed = fragment.StartsWith('#') ? fragment[1..] : fragment;
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            if (!string.Equals(part[..eq], name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = Uri.UnescapeDataString(part[(eq + 1)..]);
            return true;
        }

        return false;
    }
}
