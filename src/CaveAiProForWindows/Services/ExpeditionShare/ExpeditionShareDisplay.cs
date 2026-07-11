using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ExpeditionShare;

/// <summary>Display helpers — contract: docs/expedition-share-contract.md (website).</summary>
public static class ExpeditionShareDisplay
{
    public const long StaleBufferMs = 7_200_000;

    public enum DisplayStatus
    {
        Active,
        Overdue,
        Hidden,
    }

    public sealed class ExpeditionShareRow
    {
        public required string LeaderUid { get; init; }
        public string CaveKey { get; init; } = "";
        public string CaveName { get; init; } = "";
        public string Country { get; init; } = "";
        public double Lat { get; init; }
        public double Lon { get; init; }
        public long StartedAtMs { get; init; }
        public long? ExpectedExitAtMs { get; init; }
        public string Status { get; init; } = "";
        public string TeamLabel { get; init; } = "";
        public string LeaderDisplayName { get; init; } = "";
        public string LeaderAvatarUrl { get; init; } = "";
    }

    public static DisplayStatus ComputeDisplayStatus(ExpeditionShareRow share, long nowMs)
    {
        if (string.Equals(share.Status, "ended", StringComparison.OrdinalIgnoreCase))
            return DisplayStatus.Hidden;
        var expected = share.ExpectedExitAtMs;
        if (expected is null or <= 0)
            return string.Equals(share.Status, "active", StringComparison.OrdinalIgnoreCase)
                ? DisplayStatus.Active
                : DisplayStatus.Hidden;
        if (nowMs < expected.Value) return DisplayStatus.Active;
        if (nowMs < expected.Value + StaleBufferMs) return DisplayStatus.Overdue;
        return DisplayStatus.Hidden;
    }

    public static bool IsVisibleOnMap(ExpeditionShareRow share, long nowMs) =>
        ComputeDisplayStatus(share, nowMs) != DisplayStatus.Hidden;

    public static string LeaderDisplayName(ExpeditionShareRow share)
    {
        var snap = share.LeaderDisplayName.Trim();
        if (!string.IsNullOrEmpty(snap)) return snap;
        var team = share.TeamLabel.Trim();
        if (!string.IsNullOrEmpty(team) && !string.Equals(team, "Expedition team", StringComparison.OrdinalIgnoreCase))
            return team;
        return "Expedition team";
    }

    public static string CaveKeyForProject(CaveProjectDocument project, string? publishedDocId = null)
    {
        var pub = publishedDocId?.Trim();
        if (!string.IsNullOrEmpty(pub)) return $"pub:{pub}";
        var linked = project.LinkedLibraryCaveId?.Trim();
        if (!string.IsNullOrEmpty(linked)) return $"ref:{linked}";
        var name = project.Name?.Trim();
        if (!string.IsNullOrEmpty(name)) return $"local:{name}";
        return "local:project";
    }

    public static double? ParseCoordinate(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || string.Equals(text, "N/A", StringComparison.OrdinalIgnoreCase))
            return null;
        return double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}