namespace CaveAiProForWindows.Services;

public sealed class IntegrityReport
{
    /// <summary>No ZIP or no manifest in archive.</summary>
    public string? SkippedReason { get; set; }

    public int FilesChecked { get; private set; }
    public int FilesMatched { get; private set; }
    public int FilesMissingInZip { get; private set; }
    public List<string> Mismatches { get; } = new();

    public bool IsCompleteSuccess => SkippedReason == null && FilesMissingInZip == 0 && Mismatches.Count == 0 && FilesChecked > 0;

    internal void IncrementChecked() => FilesChecked++;
    internal void IncrementMatched() => FilesMatched++;
    internal void IncrementMissing() => FilesMissingInZip++;
}
