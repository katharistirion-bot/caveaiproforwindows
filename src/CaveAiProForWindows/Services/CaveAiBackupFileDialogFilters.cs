namespace CaveAiProForWindows.Services;

/// <summary>Windows file-dialog filters for CaveAI Pro survey backups (.json / .zip only).</summary>
public static class CaveAiBackupFileDialogFilters
{
    public const string OpenBackupTitle = "CAVE AI PRO — JSON or backup ZIP (Ctrl+click for multiple files)";

    /// <summary>WPF <see cref="Microsoft.Win32.OpenFileDialog.Filter"/> — no "All files" entry.</summary>
    public const string OpenBackupFilter =
        "CaveAI backups (*.json;*.zip)|*.json;*.zip|JSON survey (*.json)|*.json|ZIP backup (*.zip)|*.zip";

    public const string CompareBackupsFilter = "CaveAI backups (*.json;*.zip)|*.json;*.zip";
}
