namespace CaveAiProForWindows.Services.Localization;

/// <summary>UI strings — English only.</summary>
public static class AppStrings
{
    public static string MenuFile => "File";
    public static string MenuOpen => "Open…";
    public static string MenuSave => "Save project";
    public static string MenuExit => "Exit";
    public static string MenuHelp => "Help";
    public static string MenuAbout => "About";
    public static string MenuCheckUpdates => "Check for updates…";
    public static string MenuPublicLibrary => "Public Cave Library…";
    public static string MenuKeyboardShortcuts => "Keyboard shortcuts…";
    public static string MenuTools => "Tools";
    public static string MenuCompareBackups => "Compare two backups…";
    public static string MenuSurveyCompare => "Survey diff (legs/stations)…";
    public static string MenuLoopClosure => "Loop closure assistant…";
    public static string MenuRepublish => "Re-publish to Public Library…";
    public static string MenuBatchAiRender => "Batch AI render…";
    public static string MenuDownloadLibrary => "Download from Public Library…";
    public static string ToolbarOpen => "Open";
    public static string ToolbarSave => "Save";
    public static string StatusReady =>
        "Ready — open a CaveAI Pro backup (.json or .zip) for survey QC, exports, and batch workflows.";

    public static string OnboardingTitle => "Welcome to CAVE AI PRO";
    public static string OnboardingStep1Title => "Open a backup";
    public static string OnboardingStep1Body =>
        "Use File → Open or drag-and-drop a CaveAI Pro .json or .zip backup from Android.";
    public static string OnboardingStep2Title => "Plan & QC";
    public static string OnboardingStep2Body =>
        "Review traverse QC, plan/section maps, integrity checks, and exports (Survex, Therion, CSV).";
    public static string OnboardingStep3Title => "AI render";
    public static string OnboardingStep3Body =>
        "In Sketch Editor, draw or use traverse data, then AI Render for a generative cartography map.";
    public static string OnboardingStep4Title => "3D labels";
    public static string OnboardingStep4Body =>
        "On the 3D MODEL tab, use Show 3D labels and Survey labels preset. Reset 3D labels restores all Android overlays.";

    public static string OnboardingNext => "Next";
    public static string OnboardingBack => "Back";
    public static string OnboardingFinish => "Get started";
    public static string OnboardingSkip => "Skip";

    public static string Viewport3DToolsTitle => "3D tools";
    public static string Viewport3DShowLabels => "Show 3D labels";
    public static string Viewport3DLabelSize => "Label size";
    public static string Viewport3DResetLabels => "Reset 3D labels";
    public static string Viewport3DSurveyLabels => "Survey labels";
    public static string Viewport3DCleanPreset => "Clean 3D preset";

    public static string AndroidSyncReloadNow => "Reload now";
    public static string AndroidSyncDismiss => "Dismiss";

    public static string AndroidSyncBannerMessage(string fileName) =>
        $"New Android backup detected: {fileName}. Reload to refresh survey data from your phone.";

    public static string AndroidSyncBannerStatus(string fileName) =>
        $"New Android backup: {fileName}";

    public static string AndroidSyncSnackbar(string fileName) =>
        $"New backup: {fileName}";

    public static string AndroidSyncReloaded(string fileName) =>
        $"Reloaded Android backup: {fileName}";
}
