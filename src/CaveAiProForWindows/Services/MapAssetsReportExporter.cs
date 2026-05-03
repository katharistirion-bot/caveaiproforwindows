using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>UTF-8 BOM CSV report of all map-related rows (JSON/ZIP paths + standalone files).</summary>
public static class MapAssetsReportExporter
{
    public static byte[] BuildUtf8Bom(IReadOnlyList<MapAssetRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("project_name,map_label,map_type,field_category,uri_or_path,source_file,file_extension,scheme");
        foreach (var r in rows
                     .OrderBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.DetailLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.MapTypeLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.UriOrPath, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(string.Join(',',
                Csv(r.ProjectName),
                Csv(r.DetailLabel),
                Csv(r.MapTypeLabel),
                Csv(r.Category),
                Csv(r.UriOrPath),
                Csv(r.SourceFile),
                Csv(SafeFileExtension(r.UriOrPath)),
                Csv(ClassifyScheme(r.UriOrPath))));
        }

        sb.AppendLine();
        sb.AppendLine("field_category,entry_count");
        foreach (var g in rows.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine(string.Join(',', Csv(g.Key), g.Count().ToString(CultureInfo.InvariantCulture)));
        }

        sb.AppendLine();
        sb.AppendLine("metric,value");
        sb.AppendLine(string.Join(',', Csv("total_map_asset_rows"), rows.Count.ToString(CultureInfo.InvariantCulture)));
        sb.AppendLine(string.Join(',', Csv("distinct_projects"), rows.Select(r => r.ProjectName).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture)));

        var preamble = Encoding.UTF8.GetPreamble();
        return preamble.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string SafeFileExtension(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return "";
        var raw = uriOrPath.Trim();
        try
        {
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(raw, UriKind.Absolute, out var u))
                {
                    var leaf = Path.GetFileName(u.AbsolutePath);
                    return string.IsNullOrEmpty(leaf) ? "" : Path.GetExtension(leaf);
                }
            }

            if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(raw, UriKind.Absolute, out var fu))
            {
                return Path.GetExtension(fu.LocalPath);
            }
        }
        catch
        {
            return "";
        }

        var noQuery = raw.Split('?', 2)[0];
        return Path.GetExtension(noQuery);
    }

    private static string ClassifyScheme(string? uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath))
            return "";
        var s = uriOrPath.Trim();
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "https";
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "http";
        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return "file_uri";
        if (s.StartsWith("content:", StringComparison.OrdinalIgnoreCase))
            return "content_uri";
        if (Path.IsPathRooted(s))
            return "absolute_path";
        return "relative_or_other";
    }

    private static string Csv(string? x)
    {
        if (string.IsNullOrEmpty(x))
            return "";
        var t = x.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (t.Contains(',') || t.Contains('"') || t.Contains('\n') || t.Contains('\r'))
            return $"\"{t}\"";
        return t;
    }
}
