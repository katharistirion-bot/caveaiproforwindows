namespace CaveAiProForWindows.Services.SurveyCloud;

/// <summary>Firestore <c>survey_projects/{projectId}</c> metadata (private owner sync).</summary>
public sealed class SurveyCloudProjectMeta
{
    public required string ProjectId { get; init; }

    public required string CaveName { get; init; }

    public long UpdatedAtMs { get; init; }

    public int ShotCount { get; init; }

    public required string ProjectJsonStoragePath { get; init; }

    public string PlatformOrigin { get; init; } = "android";

    public string PreviewUrl =>
        $"https://www.caveaipro.com/survey/{Uri.EscapeDataString(ProjectId)}";
}
