using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Stable hash of traverse geometry for Android auto-sync conflict detection.</summary>
public static class SurveyContentFingerprint
{
    public static string Compute(CaveProjectDocument? project)
    {
        if (project == null)
            return "";

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{project.Name}|{project.Shots.Count}|");
        foreach (var s in project.Shots.OrderBy(x => x.FromStation, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.ToStation, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{s.FromStation}>{s.ToStation}>{s.Distance:0.###}>{s.Azimuth:0.###}>{s.Clino:0.###}|");
        }

        return HashText(sb.ToString());
    }

    public static string? TryComputeFromBackupZip(string zipPath, string? matchProjectName = null)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return null;

        try
        {
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry("data.json");
            if (entry == null)
                return null;

            using var sr = new StreamReader(entry.Open(), Encoding.UTF8);
            var json = sr.ReadToEnd();
            var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
            if (projects.Count == 0)
                return HashText(json);

            var pick = PickProject(projects, matchProjectName);
            return Compute(pick);
        }
        catch
        {
            return null;
        }
    }

    private static CaveProjectDocument PickProject(
        IReadOnlyList<CaveProjectDocument> projects,
        string? matchProjectName)
    {
        if (string.IsNullOrWhiteSpace(matchProjectName))
            return projects[0];

        return projects.FirstOrDefault(p =>
                   string.Equals(p.Name?.Trim(), matchProjectName.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? projects[0];
    }

    private static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
