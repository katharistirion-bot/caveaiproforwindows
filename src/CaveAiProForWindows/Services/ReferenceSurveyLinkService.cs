using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Services;

/// <summary>Persists reference catalog link metadata on a survey project (ExtensionData).</summary>
public static class ReferenceSurveyLinkService
{
    public const string ExtensionKey = "referenceCatalogLink";

    public static bool TryGetLink(CaveProjectDocument project, out ReferenceSurveyLink? link)
    {
        link = null;
        if (project.ExtensionData == null ||
            !project.ExtensionData.TryGetValue(ExtensionKey, out var el))
            return false;

        try
        {
            link = JsonSerializer.Deserialize<ReferenceSurveyLink>(el.GetRawText());
            return link != null && !string.IsNullOrWhiteSpace(link.Id);
        }
        catch
        {
            return false;
        }
    }

    public static void SetLink(CaveProjectDocument project, ReferenceCaveIndexEntry entry, ReferenceCavePin? detail = null)
    {
        project.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var link = new ReferenceSurveyLink
        {
            Id = entry.Id,
            Name = entry.Name,
            Country = entry.Country,
            Lat = entry.Lat,
            Lon = entry.Lon,
            RefCode = detail?.RefCode,
            OsmType = detail?.OsmType ?? entry.OsmType,
            OsmId = detail?.OsmId ?? entry.OsmId,
            LinkedAt = DateTimeOffset.UtcNow,
        };
        project.ExtensionData[ExtensionKey] = JsonSerializer.SerializeToElement(link);
    }

    public static string FormatSummary(ReferenceSurveyLink link) =>
        string.IsNullOrWhiteSpace(link.Country)
            ? $"{link.Name} (reference)"
            : $"{link.Name} · {link.Country} (reference)";
}

public sealed class ReferenceSurveyLink
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Country { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string? RefCode { get; set; }
    public string? OsmType { get; set; }
    public long? OsmId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}
