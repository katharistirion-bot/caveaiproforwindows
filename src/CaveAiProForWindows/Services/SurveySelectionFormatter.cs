using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Human-readable PROPERTIES / DETAILS strings for PLAN / Sketch pick results.</summary>
public static class SurveySelectionFormatter
{
    public static string FormatPick(SurveyPickResult pick, CaveProjectDocument? project)
    {
        var chainage = SurveyTraverseChainage.TryCompute(project);
        return pick switch
        {
            SurveyPickStation s => FormatStation(project, s, chainage),
            SurveyPickLeg l => FormatLeg(l, chainage),
            _ => "",
        };
    }

    private static bool NamesEq(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatStation(CaveProjectDocument? project, SurveyPickStation sn, TraverseChainageResult? chainage)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"Station: {sn.Name}");
        sb.AppendLine(
            $"Plan frame (m)  X {sn.Coord.X.ToString("0.###", inv)}  ·  Y {sn.Coord.Y.ToString("0.###", inv)}  ·  Z {sn.Coord.Z.ToString("0.###", inv)}");

        var chLine = SurveyTraverseChainage.FormatChainageLine(chainage, sn.Name, inv);
        if (!string.IsNullOrEmpty(chLine))
            sb.AppendLine(chLine);

        if (project == null)
            return sb.ToString().TrimEnd();

        var fromHere = project.Shots.Where(s => NamesEq(s.FromStation, sn.Name)).ToList();
        var trav = fromHere.FirstOrDefault(s => s.IsTraverseLeg);
        if (trav != null)
        {
            var (L, R, U, D) = trav.EffectivePlanLrud();
            sb.AppendLine(
                $"Outgoing traverse  → {trav.ToStation}  ·  tape {trav.Distance.ToString("0.###", inv)} m  ·  az {trav.Azimuth}°  ·  clino {trav.Clino}°");
            sb.AppendLine(
                $"LRUD (m)  L {L.ToString("0.##", inv)}  R {R.ToString("0.##", inv)}  U {U.ToString("0.##", inv)}  D {D.ToString("0.##", inv)}");
            AppendShotExtras(sb, trav, inv);
        }
        else
        {
            var splay = fromHere.FirstOrDefault(s => !s.IsTraverseLeg);
            if (splay != null)
            {
                var (L, R, U, D) = splay.EffectivePlanLrud();
                sb.AppendLine("Splay / side shot (to “-”):");
                sb.AppendLine(
                    $"LRUD (m)  L {L.ToString("0.##", inv)}  R {R.ToString("0.##", inv)}  U {U.ToString("0.##", inv)}  D {D.ToString("0.##", inv)}");
                AppendShotExtras(sb, splay, inv);
            }
            else if (fromHere.Count > 0)
            {
                sb.AppendLine("Other shots recorded from this station (open Data inspector for full list):");
                foreach (var s in fromHere.Take(4))
                    sb.AppendLine($"  · to {s.ToStation} — tape {s.Distance.ToString("0.##", inv)} m");
            }
            else
                sb.AppendLine("No outgoing shots from this station in current project list.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatLeg(SurveyPickLeg lg, TraverseChainageResult? chainage)
    {
        var inv = CultureInfo.InvariantCulture;
        var s = lg.Shot;
        var (L, R, U, D) = s.EffectivePlanLrud();
        var sb = new StringBuilder();
        sb.AppendLine($"Traverse leg  {s.FromStation}  →  {s.ToStation}");
        sb.AppendLine(
            $"Tape {s.Distance.ToString("0.###", inv)} m  ·  azimuth {s.Azimuth}°  ·  inclination (clino) {s.Clino}°");
        sb.AppendLine(
            $"LRUD (m)  L {L.ToString("0.##", inv)}  R {R.ToString("0.##", inv)}  U {U.ToString("0.##", inv)}  D {D.ToString("0.##", inv)}");
        sb.AppendLine(
            $"From Z {lg.FromCoord.Z.ToString("0.###", inv)} m  →  to Z {lg.ToCoord.Z.ToString("0.###", inv)} m  (plan frame)");
        var legCh = SurveyTraverseChainage.FormatLegChainageLines(chainage, s.FromStation, s.ToStation, inv);
        if (!string.IsNullOrEmpty(legCh))
            sb.AppendLine(legCh);
        AppendShotExtras(sb, s, inv);
        return sb.ToString().TrimEnd();
    }

    private static void AppendShotExtras(StringBuilder sb, ShotRecord s, CultureInfo inv)
    {
        if (Math.Abs(s.Depth) > 1e-5f)
            sb.AppendLine($"Recorded depth field: {s.Depth.ToString("0.###", inv)} m");
        if (!string.IsNullOrWhiteSpace(s.Notes))
            sb.AppendLine("Notes: " + s.Notes.Trim());
        if (!string.IsNullOrWhiteSpace(s.Symbol))
            sb.AppendLine("Symbol (JSON): " + s.Symbol.Trim());

        var temps = ShotEnvironment.TryFormatTemperatureHumidityLines(s);
        if (!string.IsNullOrWhiteSpace(temps))
            sb.AppendLine(temps.TrimEnd());

        if (s.ExtensionData?.Count > 0)
        {
            var extra = ShotEnvironment.TryFormatInterestingExtensionLines(s.ExtensionData);
            if (!string.IsNullOrWhiteSpace(extra))
                sb.AppendLine(extra.TrimEnd());
        }
    }
}

/// <summary>Pulls CaveAI Pro/Android JSON shot fields documented in README (temperature, humidity, O₂).</summary>
public static class ShotEnvironment
{
    public static string TryFormatTemperatureHumidityLines(ShotRecord s)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        if (s.ManualAmbientTempCelsius is float mt)
            sb.AppendLine($"Temperature (manual): {mt.ToString("0.##", inv)} °C");
        if (s.AmbientBleTempCelsius is float bt)
            sb.AppendLine($"Temperature (BLE): {bt.ToString("0.##", inv)} °C");
        if (s.ManualRelativeHumidityPct is float mh)
            sb.AppendLine($"Humidity (manual): {mh.ToString("0.##", inv)} %");
        if (s.AmbientBleRelativeHumidityPct is float bh)
            sb.AppendLine($"Humidity (BLE): {bh.ToString("0.##", inv)} %");
        if (s.AtmosphericO2VolPct is float o2)
            sb.AppendLine($"O₂ (vol%): {o2.ToString("0.##", inv)} %");
        return sb.ToString();
    }

    private static readonly string[] InterestingExtensionKeys =
    {
        "ambientBleTempCelsius", "manualAmbientTempCelsius", "ambientBleRelativeHumidityPct",
        "manualRelativeHumidityPct", "atmosphericO2VolPct", "co2Ppm", "barometricPressureHpa",
    };

    public static string TryFormatInterestingExtensionLines(Dictionary<string, JsonElement> ext)
    {
        var sb = new StringBuilder();
        foreach (var key in InterestingExtensionKeys)
        {
            if (!ext.TryGetValue(key, out var el))
                continue;
            var line = TryElementToHuman(key, el);
            if (!string.IsNullOrEmpty(line))
                sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string? TryElementToHuman(string key, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetDouble(out var d)
                    ? $"{HumanKey(key)}: {d.ToString(CultureInfo.InvariantCulture)}"
                    : null;
            case JsonValueKind.String:
                var s = el.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : $"{HumanKey(key)}: {s.Trim()}";
            default:
                return null;
        }
    }

    private static string HumanKey(string k) =>
        k switch
        {
            "ambientBleTempCelsius" => "Temperature (BLE) [extension]",
            "manualAmbientTempCelsius" => "Temperature (manual) [extension]",
            "ambientBleRelativeHumidityPct" => "Humidity (BLE) [extension]",
            "manualRelativeHumidityPct" => "Humidity (manual) [extension]",
            "atmosphericO2VolPct" => "O₂ (vol%) [extension]",
            "co2Ppm" => "CO₂ (ppm)",
            "barometricPressureHpa" => "Pressure (hPa)",
            _ => k,
        };
}
