using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Clears cross-session survey UI state when closing the workspace or before opening a different backup.</summary>
public static class WorkspaceSessionReset
{
    /// <summary>Surface map settings with no saved camera — next project fits its entrance instead of inheriting the previous viewport.</summary>
    public static SurfaceMapPersistedState FreshSurfaceMapState()
    {
        var current = AppUiSettingsStore.LoadOrDefault().SurfaceMap;
        return new SurfaceMapPersistedState
        {
            HillshadeEnabled = current.HillshadeEnabled,
            Terrain3dEnabled = current.Terrain3dEnabled,
            CorridorOverlayEnabled = current.CorridorOverlayEnabled,
            CopernicusDsmEnabled = current.CopernicusDsmEnabled,
            LidarOverlayEnabled = current.LidarOverlayEnabled,
            OfflineTileCacheEnabled = current.OfflineTileCacheEnabled,
            EntrancePinEnabled = current.EntrancePinEnabled,
            VehiclePinsEnabled = current.VehiclePinsEnabled,
            LidarOpacity = current.LidarOpacity,
        };
    }

    /// <summary>Resets persisted surface-map camera so the next project does not inherit the previous viewport.</summary>
    public static void ClearSurfaceMapViewport()
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        all.SurfaceMap.CenterLon = 0;
        all.SurfaceMap.CenterLat = 0;
        all.SurfaceMap.Zoom = 0;
        all.SurfaceMap.Bearing = 0;
        all.SurfaceMap.Pitch = 0;
        AppUiSettingsStore.Save(all);
    }

    /// <summary>Clears stale compare / selection state shared across map tabs.</summary>
    public static void ClearTransientSurveyUiState() =>
        SurveyStationSelectionHub.ClearAll();
}