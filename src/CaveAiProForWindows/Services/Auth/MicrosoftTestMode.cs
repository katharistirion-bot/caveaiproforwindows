namespace CaveAiProForWindows.Services.Auth;



using System.IO;

using System.Text.Json;



/// <summary>

/// Microsoft Store certification test mode (<c>MICROSOFT_TEST_MODE</c> / legacy <c>STORE_REVIEW_UNLOCKED</c>).

/// When active: skip Google sign-in, block Firebase/network cloud calls, load bundled demo survey data.

/// </summary>

public static class MicrosoftTestMode

{

#if DEBUG

    /// <summary>

    /// Quick local flip for Visual Studio Dev Run — set to <c>true</c> to bypass sign-in without editing

    /// <c>appsettings.json</c>. Ignored in Release builds.

    /// </summary>

    private const bool LocalDevOverride = false;

#else

    private const bool LocalDevOverride = false;

#endif



#if MICROSOFT_TEST_MODE || STORE_REVIEW_UNLOCKED

    private const bool CompileTimeActive = true;

#else

    private const bool CompileTimeActive = false;

#endif



    private static bool _runtimeFromAppSettings;



    /// <summary>

    /// True when certification / offline demo mode is active (compile-time review MSIX, Debug appsettings, or

    /// <see cref="LocalDevOverride"/>).

    /// </summary>

    public static bool IsActive =>

        CompileTimeActive || LocalDevOverride || _runtimeFromAppSettings;



    /// <summary>Spec alias — same flag as <see cref="IsActive"/>.</summary>

    public static bool IS_MICROSOFT_TEST_MODE => IsActive;



    public const string BannerMessage =

        "Microsoft certification test mode — offline demo (no sign-in or Firebase)";



    public const string CloudFeatureBlockedMessage =

        "Cloud features are disabled in Microsoft certification test mode. " +

        "Use File → Open for local survey workflows, or install the production Store build.";



    /// <summary>

    /// Reads <c>appsettings.json</c> next to the executable. In Debug builds, <c>"MicrosoftTestMode": true</c>

    /// enables the same behavior as the Store review MSIX. Release builds ignore appsettings (compile-time only).

    /// </summary>

    public static void InitializeFromAppSettings()

    {

#if DEBUG

        try

        {

            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(path))

                return;



            using var doc = JsonDocument.Parse(File.ReadAllText(path));

            if (doc.RootElement.TryGetProperty("MicrosoftTestMode", out var prop) &&

                prop.ValueKind is JsonValueKind.True)

            {

                _runtimeFromAppSettings = true;

            }

        }

        catch

        {

            /* malformed or missing appsettings — stay on compile-time / override flags only */

        }

#endif

    }



    /// <summary>Records review entitlement for account UI without Firestore.</summary>

    public static SubscriptionEntitlementResult CreateSyntheticEntitlement() =>

        new(

            true,

            null,

            new UserEntitlementDocument(

                "ACTIVE",

                DateTimeOffset.UtcNow.AddYears(1),

                "PLAY_SUBSCRIPTION"),

            SubscriptionAccessKind.PaidPlaySubscription);



    /// <summary>Records review entitlement for account UI without Firestore.</summary>

    public static void ActivateSession() => AccountSessionState.Apply(CreateSyntheticEntitlement());



    /// <summary>Path to bundled demo survey JSON copied next to the executable.</summary>

    public static string? ResolveBundledDemoSurveyPath()

    {

        var path = Path.Combine(

            AppContext.BaseDirectory,

            "Assets",

            "Certification",

            "certification-demo-survey.json");

        return File.Exists(path) ? path : null;

    }



    public static void ThrowIfNetworkBlocked(string feature)
    {
        if (IsActive)
            throw CreateNetworkBlockedException(feature);
    }



    public static InvalidOperationException CreateNetworkBlockedException(string feature) =>

        new($"{feature}: {CloudFeatureBlockedMessage}");

}


