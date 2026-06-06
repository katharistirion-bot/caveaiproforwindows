namespace CaveAiProForWindows.Models;

/// <summary>
/// Android <c>FieldCatalogEntryKind</c> — per-cave field log row type (Gson enum name in JSON <c>kind</c>).
/// </summary>
public enum FieldCatalogEntryKind
{
    Biota,
    Mineral,
    Depth,
    SurveyLength,
    Bacteria,
    Plant,
    Model3D,
    Anthropology,
    GeneralNote,
    Unknown,
}
