using System.Globalization;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Station easting / northing / altitude (m) in the same plan frame as the PC plan view (<see cref="SurveyStationGeometry"/>).
/// </summary>
public static class StationCoordinatesCsvExporter
{
    public static byte[] BuildUtf8Bom(CaveProjectDocument project)
    {
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project.Shots, (float)project.Alt);
        var sb = new StringBuilder();
        sb.AppendLine("station,x_m,y_m,z_m");
        foreach (var kv in coords.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var c = kv.Value;
            sb.AppendLine(string.Join(",",
                CsvEscape(c.Name),
                c.X.ToString(CultureInfo.InvariantCulture),
                c.Y.ToString(CultureInfo.InvariantCulture),
                c.Z.ToString(CultureInfo.InvariantCulture)));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var outBytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, outBytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, outBytes, preamble.Length, body.Length);
        return outBytes;
    }

    private static string CsvEscape(string s)
    {
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return s;
    }
}
