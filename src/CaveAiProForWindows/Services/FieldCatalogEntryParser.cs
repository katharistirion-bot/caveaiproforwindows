using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Parses Android <c>ProjectFieldCatalogEntry</c> JSON into unified <see cref="GeoBioRecord"/> rows with full taxonomy fields.
/// </summary>
public static class FieldCatalogEntryParser
{
    public static GeoBioRecord? TryParse(JsonElement el, string sourceLabel)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        var kind = FieldCatalogEntryKindMapper.Parse(
            el.TryGetProperty("kind", out var kEl) && kEl.ValueKind == JsonValueKind.String ? kEl.GetString() : null);

        var name = PickString(el, "name", "title", "label");
        var scientificName = PickString(el, "scientificName", "species", "taxon");
        var taxonomicGroup = PickString(el, "category");
        var station = PickString(el, "stationTag", "station", "stationName");
        var abundance = PickString(el, "abundance");
        var lifeStage = PickString(el, "lifeStage");
        var microhabitat = PickString(el, "microhabitat");
        var locationDetail = PickString(el, "locationDetail");
        var behaviorNotes = PickString(el, "behaviorNotes");
        var idConfidence = PickString(el, "idConfidence");
        var substrate = PickString(el, "substrate");
        var recordedAt = PickString(el, "recordedAt");
        var entryId = PickString(el, "id");

        var analysis = MergeAnalysisText(el);
        var title = !string.IsNullOrWhiteSpace(name)
            ? name
            : !string.IsNullOrWhiteSpace(scientificName)
                ? scientificName
                : "(unnamed catalog entry)";

        var category = kind != FieldCatalogEntryKind.Unknown
            ? FieldCatalogEntryKindMapper.ToGeoBioCategory(kind)
            : GeoBioRecordsService.InferCategoryPublic(el);

        var imgs = GeoBioRecordsService.ReadImageReferencesPublic(el);
        var coords = GeoBioRecordsService.PickCoordinatesSummaryPublic(el);
        var details = BuildStructuredDetails(
            taxonomicGroup, abundance, lifeStage, microhabitat, locationDetail,
            behaviorNotes, idConfidence, substrate, recordedAt, entryId);

        return new GeoBioRecord(
            category,
            title,
            sourceLabel,
            string.IsNullOrWhiteSpace(station) ? null : station,
            string.IsNullOrWhiteSpace(analysis) ? null : analysis,
            imgs,
            details,
            string.IsNullOrWhiteSpace(coords) ? null : coords)
        {
            FieldKind = kind != FieldCatalogEntryKind.Unknown ? kind : null,
            ScientificName = NullIfEmpty(scientificName),
            TaxonomicGroup = NullIfEmpty(taxonomicGroup),
            Abundance = NullIfEmpty(abundance),
            LifeStage = NullIfEmpty(lifeStage),
            Microhabitat = NullIfEmpty(microhabitat),
            LocationDetail = NullIfEmpty(locationDetail),
            BehaviorNotes = NullIfEmpty(behaviorNotes),
            IdConfidence = NullIfEmpty(idConfidence),
            Substrate = NullIfEmpty(substrate),
            ShortNote = NullIfEmpty(PickString(el, "note")),
            RecordedAt = NullIfEmpty(recordedAt),
            EntryId = NullIfEmpty(entryId),
        };
    }

    private static string MergeAnalysisText(JsonElement el)
    {
        var parts = new List<string>();
        void Add(string? s)
        {
            s = s?.Trim();
            if (string.IsNullOrEmpty(s))
                return;
            if (parts.Any(p => string.Equals(p, s, StringComparison.OrdinalIgnoreCase)))
                return;
            parts.Add(s);
        }

        Add(PickString(el, "extendedAnalysis"));
        Add(PickString(el, "caveAiAnalysisText", "analysisText", "analysis", "detailedDescription", "scientificInfo"));
        Add(PickString(el, "note"));

        return parts.Count == 0 ? "" : string.Join("\n\n", parts);
    }

    private static string BuildStructuredDetails(
        string taxonomicGroup, string abundance, string lifeStage, string microhabitat,
        string locationDetail, string behaviorNotes, string idConfidence, string substrate,
        string recordedAt, string entryId)
    {
        var sb = new StringBuilder();
        void Line(string label, string value)
        {
            value = value.Trim();
            if (value.Length == 0)
                return;
            sb.Append(label).Append(": ").Append(value).Append(" · ");
        }

        Line("Group", taxonomicGroup);
        Line("Abundance", abundance);
        Line("Life stage", lifeStage);
        Line("Microhabitat", microhabitat);
        Line("Location", locationDetail);
        Line("Behavior", behaviorNotes);
        Line("ID confidence", idConfidence);
        Line("Substrate", substrate);
        Line("Recorded", recordedAt);
        Line("Entry id", entryId);

        var s = sb.ToString().TrimEnd(' ', '·', ' ');
        return s;
    }

    private static string PickString(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!el.TryGetProperty(k, out var v))
                continue;
            if (v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                return v.GetString()!.Trim();
        }

        return "";
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
