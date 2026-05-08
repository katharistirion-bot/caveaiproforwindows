namespace CaveAiProForWindows.Models;

/// <summary>One traverse / topology QC line for the main SURVEY QC tab (from TraverseQcStats).</summary>
public sealed class SurveyQcIssueRow
{
    public SurveyQcIssueRow(string category, string message)
    {
        Category = category;
        Message = message;
    }

    /// <summary>Info, Topology QC, or OK.</summary>
    public string Category { get; }

    public string Message { get; }
}
