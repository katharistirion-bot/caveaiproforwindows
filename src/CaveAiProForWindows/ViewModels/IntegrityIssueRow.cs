namespace CaveAiProForWindows.ViewModels;

/// <summary>One row for the main-window Integrity tab (ZIP manifest check).</summary>
public sealed class IntegrityIssueRow
{
    public IntegrityIssueRow(string kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    /// <summary>Summary, Issue, Notice, or OK.</summary>
    public string Kind { get; }

    public string Message { get; }
}
