using System.Text.Json.Serialization;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Models;

/// <summary>Persisted UI state for map tabs (see <see cref="Services.AppUiSettingsStore"/>).</summary>
public sealed class AppUiSettingsModel
{
    /// <summary><see cref="Services.CartographicIntensity"/> name — shared by plan/sketch/section map renderers.</summary>
    public string CartographicIntensity { get; set; } = "Rich";

    public MapTabPersistedState Plan { get; set; } = new();
    public MapTabPersistedState Sketch { get; set; } = new();
    public MapTabPersistedState Section { get; set; } = new();
    public MapTabPersistedState XRay { get; set; } = new();

    /// <summary><see cref="Services.SurveyDetailDensity"/> — Minimal / Standard / Full label preset.</summary>
    public string SurveyDetailDensity { get; set; } = "Full";

    /// <summary><see cref="Services.MapExportQuality"/> — Standard (~300 DPI) or Print (~600 DPI) raster exports.</summary>
    public string MapExportQuality { get; set; } = "Standard";

    /// <summary>Non-secret generative map preferences (API token lives in Windows Credential Manager).</summary>
    public GenerativeMapSettings GenerativeMap { get; set; } = new();

    /// <summary>Android Desktop Sync folder watched by AI Analytics.</summary>
    public AndroidSyncSettings AndroidSync { get; set; } = new();

    /// <summary>First-run onboarding completed (<see cref="Views.WelcomeOnboardingWindow"/>).</summary>
    public bool HasCompletedOnboarding { get; set; }

    /// <summary>Post-sign-in wizard (sync folder, sample, tour) completed.</summary>
    public bool HasCompletedPostSignInWizard { get; set; }

    /// <summary>Overlay nearby reference catalog pins on X-Ray map when cached index is available.</summary>
    public bool ShowReferencePinsOnXRay { get; set; } = true;

    /// <summary>Overlay linked reference entrance and nearby catalog pins on the PLAN tab.</summary>
    public bool ShowReferencePinsOnPlan { get; set; } = true;

    /// <summary>Cinematic intro video shown once after login (<see cref="Views.IntroVideoWindow"/>).</summary>
    public bool HasSeenIntroVideo { get; set; }

    /// <summary>UI language code: <c>en</c> or <c>el</c>.</summary>
    public string UiLanguage { get; set; } = "en";

    /// <summary>Last shared collaboration project id for comment notifications.</summary>
    public string? CollaborationSharedProjectId { get; set; }

    /// <summary>Bump when persisted defaults need a one-time migration (see <see cref="Services.AppUiSettingsStore"/>).</summary>
    public int SettingsSchemaVersion { get; set; }
}

public static class AppUiSettingsSchema
{
    public const int Current = 6;
}

public sealed class GenerativeMapSettings
{
    public string DefaultPrompt { get; set; } =
        "photorealistic cave survey map, top-down view, rock textures, underground river";

    public bool ShowAiRenderOnCanvas { get; set; } = true;

    public double GuidanceScale { get; set; } = 9;

    /// <summary>Opacity (0–1) for generative map overlay on the X-Ray geo map.</summary>
    public double XRayAiOverlayOpacity { get; set; } = 0.52;

    /// <summary>Opacity (0–1) for generative map underlay on the Plan tab.</summary>
    public double PlanAiOverlayOpacity { get; set; } = 0.52;

    /// <summary>Last selected built-in prompt preset id (empty = custom prompt).</summary>
    public string SelectedPromptPresetId { get; set; } = "photoreal";
}

public sealed class MapTabPersistedState
{
    /// <summary><see cref="Services.MapCanvasEditorTool"/> name.</summary>
    public string Tool { get; set; } = "PanZoom";

    public bool StationNames { get; set; } = true;

    /// <summary>Plan / Sketch: Z labels. Ignored for Section.</summary>
    public bool StationZ { get; set; } = true;

    public bool Overlay { get; set; } = true;

    /// <summary>Leg tape, azimuth, clino, dZ, LRUD on traverse segments.</summary>
    public bool LegSurveyDetails { get; set; } = true;

    /// <summary>Temperature, humidity, O₂, CO₂, pressure at stations.</summary>
    public bool StationEnvironment { get; set; } = true;

    /// <summary>User depth-span braces (<c>depthSpanAnnotations</c>).</summary>
    public bool DepthSpanAnnotations { get; set; } = true;

    /// <summary>Bracket pins (<c>brackets</c>).</summary>
    public bool BracketMarkers { get; set; } = true;

    /// <summary>Android map symbol stamps on 3D viewport.</summary>
    public bool Viewport3DMapSymbols { get; set; } = true;

    /// <summary>Field catalog / geo-bio pins on 3D viewport.</summary>
    public bool Viewport3DFieldCatalog { get; set; } = true;

    /// <summary><c>stationEnvironmentSnapshots</c> on 3D viewport.</summary>
    public bool Viewport3DStationSnapshots { get; set; } = true;

    /// <summary><c>surveyAiClassifications</c> on 3D viewport.</summary>
    public bool Viewport3DAiTags { get; set; } = true;

    /// <summary>Highlight loop-closing traverse legs.</summary>
    public bool LoopClosureHighlights { get; set; } = true;

    /// <summary>Highlight LRUD ribbon / wall geometry QC issues.</summary>
    public bool LrudRibbonQcHighlights { get; set; } = true;

    /// <summary>Survey-metre coordinate grid on plan/section canvas.</summary>
    public bool ShowCoordinateGrid { get; set; }

    /// <summary>Diagonal rock hatching inside filled LRUD passage polygons.</summary>
    public bool ShowWallHatching { get; set; }

    /// <summary>Floating screen-space labels on the pseudo-3D viewport (3D MODEL tab).</summary>
    public bool Viewport3DShowLabels { get; set; }

    public bool Viewport3DShowSplines { get; set; } = true;

    public bool Viewport3DShowTopography { get; set; }

    public bool Viewport3DShowDem { get; set; }

    public bool Viewport3DSectionCutEnabled { get; set; }

    public string Viewport3DSectionCutAxis { get; set; } = "HorizontalZ";

    public double Viewport3DSectionCutPosition { get; set; } = 0.5;

    /// <summary>3D label chip scale: <see cref="Services.Viewport3DLabelSizeScale"/> Small / Medium / Large.</summary>
    public string Viewport3DLabelSize { get; set; } = Viewport3DLabelSizeScale.Small;

    /// <summary>LRUD tube mesh quality: Standard / High (empty = follow cartographic intensity).</summary>
    public string Viewport3DTubeQuality { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ZoomScale { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PanX { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PanY { get; set; }
}
