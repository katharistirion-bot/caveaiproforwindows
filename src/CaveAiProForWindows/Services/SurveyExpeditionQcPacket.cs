using System.Globalization;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyLoopQc;

namespace CaveAiProForWindows.Services;

/// <summary>
/// One text file for the expedition archive: raw legs, adjusted station coordinates, and loop closure.
/// </summary>
public static class SurveyExpeditionQcPacket
{
    public static byte[] BuildUtf8Bom(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("CaveAI Pro expedition QC packet");
        sb.AppendLine($"Cave: {project.Name}");
        sb.AppendLine($"Date: {project.Date}");
        sb.AppendLine($"Generated (local): {DateTimeOffset.Now:O}");
        var calibration = InstrumentCalibrationQuality.FormatLine(project.SurveyCalibrationProfile);
        if (calibration != null)
            sb.AppendLine(calibration);
        sb.AppendLine();

        sb.AppendLine("## Raw legs");
        sb.AppendLine("from,to,distance_m,azimuth_deg,clino_deg,flags");
        foreach (var shot in project.Shots)
        {
            var to = shot.IsTraverseLeg ? shot.ToStation : "-";
            sb.Append(Csv(shot.FromStation));
            sb.Append(',');
            sb.Append(Csv(to));
            sb.Append(',');
            sb.Append(shot.Distance.ToString("0.###", inv));
            sb.Append(',');
            sb.Append(shot.Azimuth.ToString("0.###", inv));
            sb.Append(',');
            sb.Append(shot.Clino.ToString("0.###", inv));
            sb.Append(',');
            sb.AppendLine(Csv(shot.LegFlags ?? ""));
        }

        sb.AppendLine();
        sb.AppendLine("## Adjusted stations");
        sb.AppendLine("Coordinates include loop-closure overrides when this project has them.");
        sb.AppendLine("station,x_m,y_m,z_m");
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        foreach (var station in coords.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(Csv(station.Name));
            sb.Append(',');
            sb.Append(station.X.ToString("0.###", inv));
            sb.Append(',');
            sb.Append(station.Y.ToString("0.###", inv));
            sb.Append(',');
            sb.AppendLine(station.Z.ToString("0.###", inv));
        }

        if (coords.Count == 0)
            sb.AppendLine("(no traverse stations)");

        sb.AppendLine();
        sb.AppendLine("## Loop closure");
        var loops = SurveyLoopQcAnalyzer.AnalyzeLoops(project);
        if (loops.Count == 0)
        {
            sb.AppendLine("No closed loops detected.");
        }
        else
        {
            var worst = loops.OrderByDescending(l => l.MisclosureMeters).First();
            sb.AppendLine(
                $"Loops: {loops.Count}. Worst misclosure {worst.MisclosureMeters.ToString("0.###", inv)} m " +
                $"over {worst.PathLengthMeters.ToString("0.##", inv)} m ({string.Join(" 뿯↽ ", worst.StationCycle)}).");
            foreach (var loop in loops.OrderByDescending(l => l.MisclosureMeters))
            {
                var ratio = loop.PathLengthMeters > 0.001
                    ? loop.MisclosureMeters / loop.PathLengthMeters * 100.0
                    : 0;
                sb.AppendLine(
                    $"{string.Join(" 뿯↽ ", loop.StationCycle)}: " +
                    $"{loop.MisclosureMeters.ToString("0.###", inv)} m, " +
                    $"path {loop.PathLengthMeters.ToString("0.##", inv)} m, " +
                    $"{ratio.ToString("0.##", inv)}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== End of expedition QC packet ===");
        return Utf8Bom(sb.ToString());
    }

    private static string Csv(string? value)
    {
        var s = value ?? "";
        if (s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return s;
    }

    private static byte[] Utf8Bom(string text)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, bytes, preamble.Length, body.Length);
        return bytes;
    }
}
