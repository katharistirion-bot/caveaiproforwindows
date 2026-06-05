namespace CaveAiProForWindows.Models;

/// <summary>Persisted Android ↔ Windows sync folder for AI Analytics.</summary>
public sealed class AndroidSyncSettings
{
    /// <summary>Folder containing <c>caveai_database_v1.json</c> and/or <c>android_export.json</c>.</summary>
    public string? SyncFolderPath { get; set; }

    public bool AutoSyncEnabled { get; set; } = true;

    public bool AutoRunAnalysisOnSync { get; set; } = true;

    public bool AutoRunAnalysisOnProjectLoad { get; set; } = true;
}
