using System.Globalization;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Applies station equates and fixed XYZ from the Network anchors editor into a project.
/// Shared by the WPF window and smoke tests so Apply behavior stays one path.
/// </summary>
public static class NetworkAnchorsApplier
{
    public sealed record EquateDraft(string StationA, string StationB);

    public sealed record FixedDraft(string Name, bool Hold, string? XText, string? YText, string? ZText);

    public sealed record ApplyResult(bool Ok, string? ErrorMessage);

    public static ApplyResult TryApply(
        CaveProjectDocument project,
        IEnumerable<EquateDraft> equates,
        IEnumerable<FixedDraft> fixedRows)
    {
        ArgumentNullException.ThrowIfNull(project);

        var equateList = new List<StationEquate>();
        foreach (var row in equates)
        {
            var a = (row.StationA ?? "").Trim();
            var b = (row.StationB ?? "").Trim();
            if (a.Length == 0 || b.Length == 0)
                continue;
            if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
                continue;
            equateList.Add(new StationEquate { StationA = a, StationB = b });
        }

        var held = new List<string>();
        var positions = new Dictionary<string, PlanStationPositionOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in fixedRows)
        {
            var name = (row.Name ?? "").Trim();
            if (name.Length == 0)
                continue;
            if (row.Hold && !held.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)))
                held.Add(name);

            var hasX = TryParseCoord(row.XText, out var x);
            var hasY = TryParseCoord(row.YText, out var y);
            var hasZ = TryParseCoord(row.ZText, out var z);
            if (hasX || hasY || hasZ)
            {
                if (!(hasX && hasY && hasZ))
                {
                    return new ApplyResult(
                        false,
                        $"Fixed station {name} needs X, Y, and Z together, or leave all three blank to hold without a coordinate.");
                }

                positions[name] = new PlanStationPositionOverride(x, y, z);
                if (!held.Any(h => h.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    held.Add(name);
            }
        }

        project.StationEquates = equateList.Count > 0 ? equateList : null;
        project.FixedStations = held.Count > 0 ? held : null;
        project.FixedStationPositions = positions.Count > 0 ? positions : null;
        return new ApplyResult(true, null);
    }

    public static bool TryParseCoord(string? raw, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        return float.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
