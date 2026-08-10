using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>Resolves bundled intro video path under <c>Assets/Intro/intro.mp4</c>.</summary>
public static class IntroVideoAssetPaths
{
    public const string VideoFileName = "intro.mp4";

    public static string RelativeDirectory => Path.Combine("Assets", "Intro");

    public static string RelativeVideoPath => Path.Combine(RelativeDirectory, VideoFileName);

    /// <summary>Absolute path when the file exists next to the app base directory; otherwise <c>null</c>.</summary>
    public static string? TryResolveExistingPath()
    {
        try
        {
            var path = Path.Combine(AppContentPaths.ContentRoot, RelativeVideoPath);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsVideoBundled => TryResolveExistingPath() != null;
}
