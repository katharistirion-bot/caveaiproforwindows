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
                return new AppUiSettingsModel();
            var json = File.ReadAllText(path);
            var m = JsonSerializer.Deserialize<AppUiSettingsModel>(json, JsonOptions);
            return m ?? new AppUiSettingsModel();
        }
        catch
        {
            return new AppUiSettingsModel();
        }
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
