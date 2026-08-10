using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Resolves on-disk content (Assets/) for single-file + Velopack installs.
/// AppContext.BaseDirectory points at a temp extract folder for PublishSingleFile —
/// side-by-side Assets live next to the real exe (Environment.ProcessPath).
/// </summary>
public static class AppContentPaths
{
    public static string ContentRoot
    {
        get
        {
            foreach (var candidate in CandidateRoots())
            {
                if (Directory.Exists(Path.Combine(candidate, "Assets")))
                    return candidate;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string Assets(params string[] relativeParts)
    {
        var path = Path.Combine(ContentRoot, "Assets");
        return relativeParts.Length == 0 ? path : Path.Combine(new[] { path }.Concat(relativeParts).ToArray());
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(dir))
                yield return dir;
        }

        yield return AppContext.BaseDirectory;
    }
}