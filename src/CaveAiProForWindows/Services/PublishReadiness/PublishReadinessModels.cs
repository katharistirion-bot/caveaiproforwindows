namespace CaveAiProForWindows.Services.PublishReadiness;

public enum PublishReadinessTier
{
    Required,
    Recommended,
    Info,
}

public enum PublishReadinessMode
{
    WebPublish,
    WebSync,
    WebWorkspace,
    WindowsCloud,
}

public sealed class PublishReadinessItem
{
    public required string Id { get; init; }
    public required PublishReadinessTier Tier { get; init; }
    public required string Label { get; init; }
    public string? Hint { get; init; }
    public required bool Done { get; init; }
    public required bool Required { get; init; }
    public string? Detail { get; init; }
}

public sealed class PublishReadinessResult
{
    public required IReadOnlyList<PublishReadinessItem> Items { get; init; }
    public required int ReadyCount { get; init; }
    public required int TotalCount { get; init; }
    public required int ScorePercent { get; init; }
    public required bool RequiredDone { get; init; }
    public required bool RecommendedDone { get; init; }
}

public sealed class PublishReadinessContext
{
    public PublishReadinessMode Mode { get; init; } = PublishReadinessMode.WindowsCloud;
    public Models.CaveProjectDocument? Project { get; init; }
    public bool LegalTermsAccepted { get; init; }
    public string? LinkedLibraryCaveId { get; init; }
    public string? Description { get; init; }
    public int? TopologyQcCriticalCount { get; init; }
}
