using System.Windows;

namespace CaveAiProForWindows.Services;

public enum AndroidSyncConflictChoice
{
    KeepPcSurvey,
    ReloadFromAndroid,
    SavePcCopyFirst,
}

/// <summary>Prompt when a newer Android backup would overwrite a changed PC survey.</summary>
public static class AndroidSyncConflictPrompt
{
    public static AndroidSyncConflictChoice Show(Window? owner, string zipFileName)
    {
        var message =
            $"A newer Android backup was detected ({zipFileName}), but the survey open on this PC " +
            "has different traverse data than when it was loaded.\n\n" +
            "Reloading will discard unsaved PC geometry/sketch edits that are not in the backup.\n\n" +
            "• Yes — reload from Android backup\n" +
            "• No — keep the current PC survey\n" +
            "• Cancel — save a PC backup copy first, then decide";

        var result = MessageBox.Show(
            owner,
            message,
            "Android sync conflict",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => AndroidSyncConflictChoice.ReloadFromAndroid,
            MessageBoxResult.Cancel => AndroidSyncConflictChoice.SavePcCopyFirst,
            _ => AndroidSyncConflictChoice.KeepPcSurvey,
        };
    }
}
