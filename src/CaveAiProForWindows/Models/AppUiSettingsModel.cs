using System.Text.Json.Serialization;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SketchAssist;

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

    /// <summary>MapLibre surface map (terrain around cave entrance).</summary>
    public SurfaceMapPersistedState SurfaceMap { get; set; } = new();

    /// <summary>Last Explore map URL opened in WebView2 (offline re-open hint).</summary>
    public ExploreMapPersistedState ExploreMap { get; set; } = new();

    /// <summary><see cref="Services.SurveyDetailDensity"/> — Minimal / Standard / Full label preset.</summary>
    public string SurveyDetailDensity { get; set; } = "Full";

    /// <summary><see cref="Services.MapExportQuality"/> — Standard (~300 DPI) or Print (~600 DPI) raster exports.</summary>
    public string MapExportQuality { get; set; } = "Standard";

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

    /// <summary>UI language code (English only; non-<c>en</c> values are reset on startup).</summary>
    public string UiLanguage { get; set; } = "en";

    /// <summary>Last shared collaboration project id for comment notifications.</summary>
    public string? CollaborationSharedProjectId { get; set; }

    /// <summary>Bump when persisted defaults need a one-time migration (see <see cref="Services.AppUiSettingsStore"/>).</summary>
    public int SettingsSchemaVersion { get; set; }
}

public static class AppUiSettingsSchema
{
    public const int Current = 8;
}

/// <summary>Persisted MapLibre surface-map camera and layer toggles.</summary>
public sealed class SurfaceMapPersistedState
{
    public bool HillshadeEnabled { get; set; } = true;

    /// <summary>MapLibre terrain exaggeration (0 = flat).</summary>
    public bool Terrain3dEnabled { get; set; }

    /// <summary>Survey traverse center-line overlay on the surface map.</summary>
    public bool CorridorOverlayEnabled { get; set; } = true;

    /// <summary>Copernicus GLO-30 DSM overlay (EOX tiles, client-side).</summary>
    public bool CopernicusDsmEnabled { get; set; }

    /// <summary>Georeferenced surface LiDAR / DSM raster overlay from project JSON.</summary>
    public bool LidarOverlayEnabled { get; set; } = true;

    /// <summary>Cache OSM / hillshade / DEM tiles locally for offline use (WebView2 intercept).</summary>
    public bool OfflineTileCacheEnabled { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double CenterLon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double CenterLat { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Zoom { get; set; } = 14;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Bearing { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Pitch { get; set; }
}

/// <summary>Cached Explore map WebView URL for quick re-open (online tiles still required).</summary>
public sealed class ExploreMapPersistedState
{
    /// <summary>Full https URL including viewport/layer query params when available.</summary>
    public string? LastViewportUrl { get; set; }

    /// <summary>When set, Help → Explore map opens <see cref="LastViewportUrl"/> instead of world default.</summary>
    public bool PreferLastViewport { get; set; } = true;
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

    /// <summary>Sketch Editor: snap freehand/line endpoints to traverse stations.</summary>
    public bool SnapToStations { get; set; }

    /// <summary>User ink stroke width on the design layer (DIP).</summary>
    public double InkStrokeWidthPx { get; set; } = 1.5;

    /// <summary>Dashed stroke style for freehand / line tools (generic ink only).</summary>
    public bool InkDashedStrokes { get; set; }

    /// <summary>
    /// UIS wall ink profile for line/freehand tools:
    /// <see cref="SketchWallInkProfiles.Wall"/>, <see cref="SketchWallInkProfiles.WallEstimated"/>,
    /// <see cref="SketchWallInkProfiles.FillBoundary"/>, or <see cref="SketchWallInkProfiles.Ink"/>.
    /// </summary>
    public string InkWallProfile { get; set; } = SketchWallInkProfiles.Wall;

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