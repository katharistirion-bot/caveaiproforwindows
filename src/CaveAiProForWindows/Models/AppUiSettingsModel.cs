using System.Text.Json.Serialization;

namespace CaveAiProForWindows.Models;

/// <summary>Persisted UI state for map tabs (see <see cref="Services.AppUiSettingsStore"/>).</summary>
public sealed class AppUiSettingsModel
{
    /// <summary><see cref="Services.CartographicIntensity"/> name — shared by plan/sketch/section map renderers.</summary>
    public string CartographicIntensity { get; set; } = "Balanced";

    public MapTabPersistedState Plan { get; set; } = new();
    public MapTabPersistedState Sketch { get; set; } = new();
    public MapTabPersistedState Section { get; set; } = new();

    /// <summary>Non-secret generative map preferences (API token lives in Windows Credential Manager).</summary>
    public GenerativeMapSettings GenerativeMap { get; set; } = new();

    /// <summary>Android Desktop Sync folder watched by AI Analytics.</summary>
    public AndroidSyncSettings AndroidSync { get; set; } = new();
}

public sealed class GenerativeMapSettings
{
    public string DefaultPrompt { get; set; } =
        "photorealistic cave survey map, top-down view, rock textures, underground river";

    public bool ShowAiRenderOnCanvas { get; set; } = true;
}

public sealed class MapTabPersistedState
{
    /// <summary><see cref="Services.MapCanvasEditorTool"/> name.</summary>
    public string Tool { get; set; } = "PanZoom";

    public bool StationNames { get; set; } = true;

    /// <summary>Plan / Sketch: Z labels. Ignored for Section.</summary>
    public bool StationZ { get; set; }

    public bool Overlay { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double ZoomScale { get; set; } = 1;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PanX { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double PanY { get; set; }
}
