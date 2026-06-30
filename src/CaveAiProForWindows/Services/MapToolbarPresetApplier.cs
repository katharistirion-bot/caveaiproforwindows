using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class MapToolbarPresetApplier
{
    public enum Preset { Publication, FieldQc, Minimal }

    public static void Apply(string tag, AppUiSettingsModel all)
    {
        var preset = tag switch
        {
            "Publication" => Preset.Publication,
            "Field QC" => Preset.FieldQc,
            "Minimal" => Preset.Minimal,
            _ => (Preset?)null,
        };
        if (preset == null)
            return;
        ApplyToModel(preset.Value, all);
        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Apply(Preset preset)
    {
        var all = AppUiSettingsStore.LoadOrDefault();
        ApplyToModel(preset, all);
        AppUiSettingsStore.Save(all);
        SettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void ApplyToModel(Preset preset, AppUiSettingsModel all)
    {
        switch (preset)
        {
            case Preset.Publication:
                all.CartographicIntensity = CartographicIntensityParser.ToPersistedString(CartographicIntensity.Balanced);
                all.SurveyDetailDensity = SurveyDetailDensityParser.ToPersistedString(SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Plan, SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Section, SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Sketch, SurveyDetailDensity.Minimal);
                all.Plan.ShowCoordinateGrid = false;
                all.Section.ShowCoordinateGrid = false;
                break;
            case Preset.FieldQc:
                all.CartographicIntensity = CartographicIntensityParser.ToPersistedString(CartographicIntensity.Rich);
                all.SurveyDetailDensity = SurveyDetailDensityParser.ToPersistedString(SurveyDetailDensity.Full);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Plan, SurveyDetailDensity.Full);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Section, SurveyDetailDensity.Full);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Sketch, SurveyDetailDensity.Full);
                all.Plan.LoopClosureHighlights = true;
                all.Plan.LrudRibbonQcHighlights = true;
                all.Plan.ShowCoordinateGrid = true;
                break;
            default:
                all.CartographicIntensity = CartographicIntensityParser.ToPersistedString(CartographicIntensity.Subtle);
                all.SurveyDetailDensity = SurveyDetailDensityParser.ToPersistedString(SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Plan, SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Section, SurveyDetailDensity.Minimal);
                SurveyDetailDensityMapper.ApplyToMapTab(all.Sketch, SurveyDetailDensity.Minimal);
                all.Plan.ShowCoordinateGrid = false;
                all.Plan.LoopClosureHighlights = false;
                all.Plan.LrudRibbonQcHighlights = false;
                break;
        }
    }

    public static event EventHandler? SettingsChanged;
}