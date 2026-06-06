using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.ViewModels;

/// <summary>One row in the GEO &amp; BIO field-catalog analytical DataGrid.</summary>
public sealed class FieldCatalogTableRowViewModel
{
    public FieldCatalogTableRowViewModel(GeoBioRecord record)
    {
        Kind = record.FieldKind.HasValue
            ? FieldCatalogEntryKindMapper.DisplayLabel(record.FieldKind.Value)
            : "—";
        Name = record.Title;
        ScientificName = record.ScientificName ?? "";
        TaxonomicGroup = record.TaxonomicGroup ?? "";
        Station = record.Station ?? "";
        Abundance = record.Abundance ?? "";
        LifeStage = record.LifeStage ?? "";
        Microhabitat = record.Microhabitat ?? "";
        Substrate = record.Substrate ?? "";
        IdConfidence = record.IdConfidence ?? "";
        LocationDetail = record.LocationDetail ?? "";
        BehaviorNotes = record.BehaviorNotes ?? "";
        RecordedAt = record.RecordedAt ?? "";
        HasPhoto = record.ImageReferences.Count > 0 ? "Yes" : "";
        AnalysisPreview = Truncate(record.CaveAiAnalysisText ?? record.ShortNote ?? "", 240);
        Source = record.SourceLabel;
    }

    public string Kind { get; }
    public string Name { get; }
    public string ScientificName { get; }
    public string TaxonomicGroup { get; }
    public string Station { get; }
    public string Abundance { get; }
    public string LifeStage { get; }
    public string Microhabitat { get; }
    public string Substrate { get; }
    public string IdConfidence { get; }
    public string LocationDetail { get; }
    public string BehaviorNotes { get; }
    public string RecordedAt { get; }
    public string HasPhoto { get; }
    public string AnalysisPreview { get; }
    public string Source { get; }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
