namespace CaveAiProForWindows.Services.Localization;

using CaveAiProForWindows.Services.Auth;

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

    public static string OnboardingTitle => "GETTING STARTED";
    public static string OnboardingStep0Title => "One subscription, two platforms";
    public static string OnboardingStep0Body =>
        "CaveAI Pro on Android is your field hub. This Desktop Companion extends the same projects with " +
        "large-screen QC, publication exports, and cloud tools. Sign in with the same Google account used on Android.";
    public static string OnboardingStep1Title => "Open your survey data";
    public static string OnboardingStep1Body =>
        "File → Open, or drag a CaveAI Pro backup (.json or .zip) from your phone or sync folder. " +
        "Full ZIP archives include photos, maps, and integrity manifests.";
    public static string OnboardingStep2Title => "Plan, section & survey QC";
    public static string OnboardingStep2Body =>
        "Inspect traverse quality, loop closure, plan and section views, and integrity checks. " +
        "Export to Survex, Therion, DXF, SVG, and office PDF reports.";
    public static string OnboardingStep3Title => "Generative cartography";
    public static string OnboardingStep3Body =>
        "Sketch Editor captures structure masks from your survey. AI Render produces publication-ready " +
        "maps via CaveAI cloud — ideal for brochures and team review.";
    public static string OnboardingStep4Title => "3D labels & publication";
    public static string OnboardingStep4Body =>
        "Use the 3D MODEL tab for labeled overviews, then Tools → Publication sheet for composite plan, " +
        "elevation, and metadata exports ready to share.";

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
    public static string Viewport3DCompetitivePreset => "Competitive 3D";

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

    public static string AccountBannerDismiss => "Dismiss";

    public static string AccountBannerManagePlay =>
        "Manage subscription (Google Play)";

    public static string AccountBannerSubscribePlay =>
        "Subscribe on Google Play (Android app required)";

    public static string SubscriptionRequiredTitle => "Subscription required";

    public static string SubscriptionRequiredBody =>
        "An active CaveAI Pro subscription through Google Play is required for the Desktop Companion. " +
        "Subscribe on Android with the same Google account you use here.";

    public static string SubscriptionRequiredPlayLink =>
        $"Get CaveAI Pro: {AccountLinks.PlayStoreAppUrl}";

    public static string MenuReferenceCatalog => "Public Cave _Library (reference catalog)…";
    public static string MenuFieldTripPlanner => "Field _Trip Planner…";
    public static string MenuBatchSurveyQc => "Batch survey _QC…";
    public static string MenuExportDiagnosticBundle => "Export _diagnostic bundle…";

    public static string PostSignInStep0Title => "Android sync folder";
    public static string PostSignInStep0Body =>
        "Optional: set the folder where Android Desktop Sync writes CaveAI_Backup_*.zip files. " +
        "The app watches this folder and can reload new backups automatically.";
    public static string PostSignInStep1Title => "Open a sample survey";
    public static string PostSignInStep1Body =>
        "Try the built-in certification demo survey, or skip and open your own Android backup later (File → Open).";
    public static string PostSignInStep2Title => "Reference catalog";
    public static string PostSignInStep2Body =>
        "Browse 60k+ reference caves offline from Help → Public Cave Library. Filter by country, search by name, or plan a field trip.";
    public static string PostSignInStep3Title => "You're ready";
    public static string PostSignInStep3Body =>
        "Replay the intro video anytime from Help → Intro video. Use Tools → Field Trip Planner to build an itinerary from reference caves.";

    public static string ReferenceLinkedSummary(string summary) => $"Reference link: {summary}";

    public static string LoginAccessDeniedPlayStore => "Get CaveAI Pro on Google Play";

    public static string LoginAccessDeniedPublicLibrary => "Browse Public Library (free)";

    public static string LoginAccessDeniedPlayHint =>
        "CaveAI Pro subscriptions are purchased through Google Play on Android. After subscribing, sign in here with the same Google account.";

    /// <summary>Shown on Microsoft Store builds to guide certification testers.</summary>
    public static string LoginCertificationTesterHint =>
        "Microsoft Store certification testers: use the Google account and password provided in Partner Center submission notes (not a personal account).";

    /// <summary>Shown on Microsoft certification test MSIX builds (<c>MICROSOFT_TEST_MODE</c>).</summary>
    public static string StoreReviewBuildBanner => MicrosoftTestMode.BannerMessage;

    public static string MenuSwitchAccount => "Switch _Google account…";
}
