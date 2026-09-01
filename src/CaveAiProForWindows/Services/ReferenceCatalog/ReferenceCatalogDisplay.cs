using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Display helpers for reference catalog list/detail — web/Android parity.</summary>
public static class ReferenceCatalogDisplay
{
    public const string ReferenceOnlyBadge = "Reference only";
    public const string RichOsmBadge = "Rich OSM metadata";

    public static bool HasRichMetadata(ReferenceCaveIndexEntry entry)
    {
        if (entry.DepthM is > 0 || entry.LengthM is > 0) return true;
        if (entry.ElevationM is double e && double.IsFinite(e)) return true;
        if (!string.IsNullOrWhiteSpace(entry.Region)) return true;
        if (!string.IsNullOrWhiteSpace(entry.Preview)) return true;
        if (!string.IsNullOrWhiteSpace(entry.CaveType) &&
            !entry.CaveType.Equals("Cave entrance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return entry.Rich;
    }

    public static bool HasRichMetadata(ReferenceCavePin pin)
    {
        if (pin.DepthM is > 0 || pin.LengthM is > 0) return true;
        if (pin.ElevationM is double e && double.IsFinite(e)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Region)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Description)) return true;
        if (!string.IsNullOrWhiteSpace(pin.AccessNote)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Website)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Wikipedia)) return true;
        if (!string.IsNullOrWhiteSpace(pin.RefCode)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Period) || !string.IsNullOrWhiteSpace(pin.Significance)) return true;
        if (!string.IsNullOrWhiteSpace(pin.Finds) || !string.IsNullOrWhiteSpace(pin.Heritage)) return true;
        if (!string.IsNullOrWhiteSpace(pin.CaveType) &&
            !pin.CaveType.Equals("Cave entrance", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return pin.Rich;
    }

    public static string ListBadge(ReferenceCaveIndexEntry entry) =>
        HasRichMetadata(entry) ? RichOsmBadge : ReferenceOnlyBadge;

    public static string ListSummary(ReferenceCaveIndexEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Country)) parts.Add(entry.Country.Trim());
        if (!string.IsNullOrWhiteSpace(entry.Region)) parts.Add(entry.Region.Trim());
        if (!string.IsNullOrWhiteSpace(entry.CaveType) &&
            !entry.CaveType.Equals("Cave entrance", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(entry.CaveType.Trim());
        }
        if (entry.DepthM is > 0) parts.Add($"{entry.DepthM:0.#} m deep");
        if (entry.LengthM is > 0) parts.Add($"{entry.LengthM:0.#} m long");
        return string.Join(" · ", parts);
    }

    public static string PreviewText(ReferenceCaveIndexEntry entry, int maxLen = 120)
    {
        var text = (entry.Preview ?? "").Replace('\n', ' ').Trim();
        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(text))
            text = BuildSynthesizedPreview(entry);
        if (text.Length <= maxLen) return text;
        return text[..(maxLen - 1)] + "…";
    }

    public static string BuildSynthesizedPreview(ReferenceCaveIndexEntry entry)
    {
        var name = string.IsNullOrWhiteSpace(entry.Name) ? "Cave entrance" : entry.Name.Trim();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.CaveType) &&
            !entry.CaveType.Equals("Cave entrance", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"{name} is listed as a {entry.CaveType.ToLowerInvariant()}.");
        }
        else
        {
            parts.Add($"{name} is a natural cave entrance in the OpenStreetMap reference catalog.");
        }

        var loc = ListSummary(entry);
        if (!string.IsNullOrWhiteSpace(loc))
            parts.Add(loc.EndsWith('.') ? loc : loc + ".");
        return string.Join(" ", parts);
    }

    public static string PinDescription(ReferenceCavePin pin)
    {
        if (!string.IsNullOrWhiteSpace(pin.Description))
            return pin.Description.Trim();
        return BuildSynthesizedPreview(new ReferenceCaveIndexEntry
        {
            Name = pin.DisplayName,
            Country = pin.Country,
            Region = pin.Region,
            CaveType = pin.CaveType,
            DepthM = pin.DepthM,
            LengthM = pin.LengthM,
            ElevationM = pin.ElevationM,
        });
    }

    /// <summary>Structured archaeological notes (web referenceArchaeologicalNotes parity).</summary>
    public static IReadOnlyList<(string Label, string Value)> ArchaeologicalNotes(ReferenceCavePin pin)
    {
        var notes = new List<(string Label, string Value)>();
        if (!string.IsNullOrWhiteSpace(pin.Period))
            notes.Add(("Period", pin.Period.Trim()));
        if (!string.IsNullOrWhiteSpace(pin.Significance))
            notes.Add(("Significance", pin.Significance.Trim()));
        if (!string.IsNullOrWhiteSpace(pin.Finds))
            notes.Add(("Key finds", pin.Finds.Trim()));
        if (!string.IsNullOrWhiteSpace(pin.Heritage))
            notes.Add(("Heritage", pin.Heritage.Trim()));
        return notes;
    }
}
