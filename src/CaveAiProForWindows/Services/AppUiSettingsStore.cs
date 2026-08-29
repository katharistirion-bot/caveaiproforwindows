using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class AppUiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaveAiProForWindows",
        "ui-settings.json");

    public static AppUiSettingsModel LoadOrDefault()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return CreateFresh();

            var json = File.ReadAllText(path);
            var m = JsonSerializer.Deserialize<AppUiSettingsModel>(json, JsonOptions);
            m ??= CreateFresh();
            var changed = TryMigrate(m);
            // Product is English-only worldwide — clear any legacy "el" UI language.
            if (!string.Equals(m.UiLanguage, "en", StringComparison.OrdinalIgnoreCase))
            {
                m.UiLanguage = "en";
                changed = true;
            }
            if (changed)
                Save(m);
            return m;
        }
        catch
        {
            return CreateFresh();
        }
    }

    private static AppUiSettingsModel CreateFresh()
    {
        return new AppUiSettingsModel { SettingsSchemaVersion = AppUiSettingsSchema.Current };
    }

    private static bool TryMigrate(AppUiSettingsModel model)
    {
        if (model.SettingsSchemaVersion >= AppUiSettingsSchema.Current)
            return false;

        if (model.SettingsSchemaVersion < 5)
            ApplyViewport3DCleanDefaults(model);

        model.SettingsSchemaVersion = AppUiSettingsSchema.Current;
        return true;
    }

    /// <summary>Usable 3D defaults: labels off, small chips, minimal overlay clutter.</summary>
    public static void ApplyViewport3DCleanDefaults(AppUiSettingsModel model)
    {
        var p = model.Plan;
        p.Viewport3DShowLabels = false;
        p.Viewport3DLabelSize = Viewport3DLabelSizeScale.Small;
        p.Viewport3DMapSymbols = false;
        p.Viewport3DFieldCatalog = false;
        p.Viewport3DStationSnapshots = false;
        p.Viewport3DAiTags = false;
    }

    /// <summary>Enable all 3D viewport labels including Android map symbols, field catalog, snapshots, and AI tags.</summary>
    public static void ApplyViewport3DFullLabels(AppUiSettingsModel model)
    {
        var p = model.Plan;
        p.Viewport3DShowLabels = true;
        p.Viewport3DMapSymbols = true;
        p.Viewport3DFieldCatalog = true;
        p.Viewport3DStationSnapshots = true;
        p.Viewport3DAiTags = true;
        p.StationNames = true;
        p.LegSurveyDetails = true;
        p.StationEnvironment = true;
        p.DepthSpanAnnotations = true;
        p.BracketMarkers = true;
    }

    /// <summary>Reset only 3D label toggles and persist (user-facing reset).</summary>
    public static void ResetViewport3DLabels()
    {
        var all = LoadOrDefault();
        ApplyViewport3DFullLabels(all);
        Save(all);
    }

    public static void Save(AppUiSettingsModel model)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(model, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>After Android backup import, enable full survey overlays on all map tabs.</summary>
    public static void ApplyFullOverlaysAfterImport()
    {
        var all = LoadOrDefault();
        all.SurveyDetailDensity = SurveyDetailDensityParser.ToPersistedString(SurveyDetailDensity.Full);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Plan, SurveyDetailDensity.Full);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Sketch, SurveyDetailDensity.Full);
        SurveyDetailDensityMapper.ApplyToMapTab(all.Section, SurveyDetailDensity.Full);
        Save(all);
    }
}
