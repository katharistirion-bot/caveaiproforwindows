namespace CaveAiProForWindows.Services.Collaboration;

public sealed class ProjectComment
{
    public required string CommentId { get; init; }
    public required string AuthorUid { get; init; }
    public string AuthorDisplayName { get; init; } = "";
    public required string Text { get; init; }
    public long CreatedAtMs { get; init; }
}

public sealed class SharedProjectInfo
{
    public required string ProjectId { get; init; }
    public required string OwnerUid { get; init; }
    public required string ProjectName { get; init; }
    public IReadOnlyList<string> MemberUids { get; init; } = Array.Empty<string>();
    public string? SurveyJsonUrl { get; init; }
    public long UpdatedAtMs { get; init; }
}
