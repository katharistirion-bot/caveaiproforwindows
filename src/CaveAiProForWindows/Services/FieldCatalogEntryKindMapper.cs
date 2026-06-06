using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Maps Android Gson enum strings to <see cref="FieldCatalogEntryKind"/>.</summary>
public static class FieldCatalogEntryKindMapper
{
    public static FieldCatalogEntryKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return FieldCatalogEntryKind.Unknown;
        return raw.Trim().ToUpperInvariant() switch
        {
            "BIOTA" => FieldCatalogEntryKind.Biota,
            "MINERAL" => FieldCatalogEntryKind.Mineral,
            "DEPTH" => FieldCatalogEntryKind.Depth,
            "SURVEY_LENGTH" => FieldCatalogEntryKind.SurveyLength,
            "BACTERIA" => FieldCatalogEntryKind.Bacteria,
            "PLANT" => FieldCatalogEntryKind.Plant,
            "MODEL_3D" => FieldCatalogEntryKind.Model3D,
            "ANTHROPOLOGY" => FieldCatalogEntryKind.Anthropology,
            "GENERAL_NOTE" => FieldCatalogEntryKind.GeneralNote,
            _ => FieldCatalogEntryKind.Unknown,
        };
    }

    public static string DisplayLabel(FieldCatalogEntryKind kind) => kind switch
    {
        FieldCatalogEntryKind.Biota => "Biota / fauna",
        FieldCatalogEntryKind.Mineral => "Mineral / geology",
        FieldCatalogEntryKind.Depth => "Depth note",
        FieldCatalogEntryKind.SurveyLength => "Survey length",
        FieldCatalogEntryKind.Bacteria => "Bacteria / microbiology",
        FieldCatalogEntryKind.Plant => "Plant / flora",
        FieldCatalogEntryKind.Model3D => "3D scan / model",
        FieldCatalogEntryKind.Anthropology => "Anthropology / archaeology",
        FieldCatalogEntryKind.GeneralNote => "General note",
        _ => "Other",
    };

    public static GeoBioCategory ToGeoBioCategory(FieldCatalogEntryKind kind) => kind switch
    {
        FieldCatalogEntryKind.Mineral => GeoBioCategory.Rock,
        FieldCatalogEntryKind.Biota or FieldCatalogEntryKind.Bacteria or FieldCatalogEntryKind.Plant
            => GeoBioCategory.Organism,
        FieldCatalogEntryKind.Anthropology => GeoBioCategory.Organism,
        _ => GeoBioCategory.Other,
    };
}
