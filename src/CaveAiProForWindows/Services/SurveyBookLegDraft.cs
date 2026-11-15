using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Next blank traverse leg for the survey book.</summary>
public static class SurveyBookLegDraft
{
    public static ShotRecord CreateNext(IReadOnlyList<ShotRecord>? shots)
    {
        var list = shots ?? Array.Empty<ShotRecord>();
        var last = list.LastOrDefault(s => s.IsTraverseLeg);
        var from = string.IsNullOrWhiteSpace(last?.ToStation) ? "1" : last!.ToStation.Trim();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shot in list)
        {
            if (!string.IsNullOrWhiteSpace(shot.FromStation))
                used.Add(shot.FromStation.Trim());
            if (shot.IsTraverseLeg && !string.IsNullOrWhiteSpace(shot.ToStation))
                used.Add(shot.ToStation.Trim());
        }

        var to = NextStationLabel(from);
        var guard = 0;
        while (used.Contains(to) && guard++ < 1000)
            to = NextStationLabel(to);

        return new ShotRecord
        {
            Id = Guid.NewGuid().ToString(),
            FromStation = from,
            ToStation = to,
            Distance = 0f,
            Azimuth = 0f,
            Clino = 0f,
        };
    }

    public static string NextStationLabel(string station)
    {
        var text = station.Trim();
        if (text.Length == 0)
            return "2";
        var i = text.Length;
        while (i > 0 && char.IsDigit(text[i - 1]))
            i--;
        if (i == text.Length)
            return text + "2";
        var prefix = text[..i];
        if (!int.TryParse(text[i..], out var number))
            return text + "2";
        return prefix + (number + 1).ToString();
    }
}
