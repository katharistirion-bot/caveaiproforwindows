using System.Windows;

namespace CaveAiProForWindows.Services;

/// <summary>Prompts the user when a new Android backup ZIP appears in the sync folder.</summary>
public static class AndroidBackupOpenPrompt
{
    public static bool AskOpenBackup(Window? owner, string zipFileName)
    {
        var result = MessageBox.Show(
            owner,
            $"A new Android backup was detected:\n{zipFileName}\n\nOpen this backup now?",
            "Android backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public static bool AskProjectNameConflict(Window? owner, string projectName, string zipFileName)
    {
        var result = MessageBox.Show(
            owner,
            $"The backup \"{zipFileName}\" contains a survey named \"{projectName}\" that is already open on this PC, " +
            "and the backup file is newer than the last reload.\n\n" +
            "Reload from Android? Unsaved PC edits may be lost.\n\n" +
            "• Yes — reload from backup\n" +
            "• No — keep current PC survey",
            "Android sync — same project name",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }
}
