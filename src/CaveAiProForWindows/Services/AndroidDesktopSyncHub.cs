namespace CaveAiProForWindows.Services;

/// <summary>Notifies the main window when Android Desktop Sync settings or files change.</summary>
public static class AndroidDesktopSyncHub
{
    public static event EventHandler? SyncSettingsChanged;

    public static event EventHandler<AndroidSyncFilesChangedEventArgs>? SyncFilesChanged;

    public static event EventHandler<string>? CollaborationProjectChanged;

    public static void NotifySyncSettingsChanged() =>
        SyncSettingsChanged?.Invoke(null, EventArgs.Empty);

    public static void NotifySyncFilesChanged(string syncFolder, string? changedFileName = null) =>
        SyncFilesChanged?.Invoke(null, new AndroidSyncFilesChangedEventArgs(syncFolder, changedFileName));

    public static void NotifyCollaborationProjectChanged(string projectId) =>
        CollaborationProjectChanged?.Invoke(null, projectId);
}

public sealed class AndroidSyncFilesChangedEventArgs : EventArgs
{
    public AndroidSyncFilesChangedEventArgs(string syncFolder, string? changedFileName)
    {
        SyncFolder = syncFolder;
        ChangedFileName = changedFileName;
    }

    public string SyncFolder { get; }
    public string? ChangedFileName { get; }
}
