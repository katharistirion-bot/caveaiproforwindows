using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>CSV export for AI Analytics diagnostic Results grid.</summary>
public static class AiAnalyticsDiagnosticExporter
{
    public static byte[] BuildCsvUtf8Bom(IEnumerable<AiAnalyticsMetricRow> rows, string? title = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine(CsvEscape($"# {title.Trim()}"));
        sb.AppendLine("Station,Android,Geometry,Finding,Detail,Alert");

        foreach (var r in rows)
        {
            sb.Append(CsvEscape(r.Station)).Append(',')
                .Append(CsvEscape(r.AndroidContext)).Append(',')
                .Append(CsvEscape(r.GeometryContext)).Append(',')
                .Append(CsvEscape(r.Metric)).Append(',')
                .Append(CsvEscape(r.Value)).Append(',')
                .Append(CsvEscape(r.AlertLabel))
                .AppendLine();
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string CsvEscape(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
