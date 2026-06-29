namespace CaveAiProForWindows.Models;

public sealed class SurveyHealthSnapshot
{
    public string ProjectName { get; init; } = "";
    public int StationCount { get; init; }
    public int TraverseLegCount { get; init; }
    public int SplayCount { get; init; }
    public double MaxDepthM { get; init; }
    public double TraverseLengthM { get; init; }
    public double LongestBranchM { get; init; }
    public int LoopCount { get; init; }
    public double WorstLoopMisclosureM { get; init; }
    public string WorstLoopLabel { get; init; } = "";
    public int MissingLrudLegCount { get; init; }
    public int QcWarningCount { get; init; }
    public int AnomalyCount { get; init; }
    public bool EntranceGpsLocked { get; init; }
    public bool QcPassReadyForSketch { get; init; }
    public string HealthSummary { get; init; } = "";
}

public sealed class SurveyBackupDiff
{
    public bool HasComparison { get; init; }
    public string PreviousBackupName { get; init; } = "";
    public string CurrentBackupName { get; init; } = "";
    public int AddedStationCount { get; init; }
    public int AddedLegCount { get; init; }
    public IReadOnlyList<string> AddedStationNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AddedLegLabels { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = "No previous backup in sync folder to compare.";
}

public sealed class SurveyIntelligenceInsight
{
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public AiAnalyticsAlertLevel Severity { get; init; }
}

public sealed class SurveyBatchQcRow
{
    public string ProjectName { get; init; } = "";
    public string SourceFile { get; init; } = "";
    public int Shots { get; init; }
    public int Anomalies { get; init; }
    public int Loops { get; init; }
    public string Status { get; init; } = "";
}