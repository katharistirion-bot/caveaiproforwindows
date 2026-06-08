using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.CloudPublish;

/// <summary>Validates bundled firebase-config.json before release packaging and at runtime diagnostics.</summary>
public static class FirebaseBundledConfigGuard
{
    public const string PlaceholderMarker = "REPLACE_AT_BUILD";

    public static bool IsUsableFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var cfg = JsonSerializer.Deserialize<FirebaseWebClientConfig>(File.ReadAllText(path));
            return cfg is { IsUsable: true };
        }
        catch
        {
            return false;
        }
    }

    public static bool IsUsableJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var cfg = JsonSerializer.Deserialize<FirebaseWebClientConfig>(json);
            return cfg is { IsUsable: true };
        }
        catch
        {
            return false;
        }
    }

    public static string DefaultBundledConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "DesktopAuth", "firebase-config.json");

    public static bool IsCurrentBundledConfigUsable() =>
        FirebaseProjectConfig.LoadFromEnvironment().ToWebClientConfig().IsUsable
        || IsUsableFile(DefaultBundledConfigPath);

    public static string ReleaseBuildFailureMessage =>
        "Release build blocked: Assets/DesktopAuth/firebase-config.json still contains REPLACE_AT_BUILD.\n" +
        "Set CAVEAIPRO_FIREBASE_API_KEY and run tools/inject-firebase-config.ps1 before packaging.\n" +
        "See docs/SECURITY.md.";
}
