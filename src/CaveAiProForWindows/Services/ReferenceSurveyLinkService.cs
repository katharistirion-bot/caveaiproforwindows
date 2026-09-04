using System.Globalization;
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

    /// <summary>Android <c>ReferenceCaveSurveyStarter.findExistingProject</c> — same <c>referenceCatalogLink.id</c>.</summary>
    public static CaveProjectDocument? FindExistingProject(
        IEnumerable<CaveProjectDocument> projects,
        string referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
            return null;
        foreach (var project in projects)
        {
            if (TryGetLink(project, out var link) &&
                string.Equals(link!.Id, referenceId, StringComparison.OrdinalIgnoreCase))
                return project;
        }

        return null;
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

    /// <summary>
    /// Empty survey project + Cave Library card, matching Android <c>ReferenceCaveSurveyStarter.buildNewProject</c>.
    /// </summary>
    public static (CaveProjectDocument Project, KnownCaveRecord LibraryCard) CreateSurveyWorkspace(
        ReferenceCaveIndexEntry entry,
        ReferenceCavePin? detail = null)
    {
        var name = string.IsNullOrWhiteSpace(entry.Name) ? "Reference survey" : entry.Name.Trim();
        var now = DateTime.Now;
        var siteType = SurveySiteType.InferFromCatalogLabel(detail?.CaveType ?? entry.CaveType);
        var elevation = detail?.ElevationM ?? entry.ElevationM;
        var alt = elevation is > 0 and not double.NaN and not double.PositiveInfinity and not double.NegativeInfinity
            ? elevation.Value
            : 0;
        var libraryId = Guid.NewGuid().ToString();
        var area = string.Join(" · ", new[] { entry.Region, entry.Country }
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(area))
            area = "Reference catalog";

        var project = new CaveProjectDocument
        {
            Name = name,
            Date = now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            StartTime = now.ToString("HH:mm", CultureInfo.InvariantCulture),
            Lat = entry.Lat,
            Lon = entry.Lon,
            Alt = alt,
            LinkedLibraryCaveId = libraryId,
            SurveySiteType = siteType,
            SurveyArchiveSchemaVersion = "2",
            RequireVehicleParkStep = false,
            ProjectId = Guid.NewGuid().ToString(),
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        };
        SetLink(project, entry, detail);

        var libraryCard = new KnownCaveRecord
        {
            Id = libraryId,
            Name = name,
            Lat = entry.Lat,
            Lon = entry.Lon,
            Elevation = alt,
            Depth = detail?.DepthM ?? entry.DepthM ?? 0,
            Length = detail?.LengthM ?? entry.LengthM ?? 0,
            Area = area,
            Type = siteType,
            Description = string.IsNullOrWhiteSpace(detail?.Description)
                ? (entry.Preview ?? "")
                : detail!.Description.Trim(),
            DateAdded = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        return (project, libraryCard);
    }

    /// <summary>Empty survey project with entrance coords and referenceCatalogLink pre-filled.</summary>
    public static CaveProjectDocument CreateSurveyProject(ReferenceCaveIndexEntry entry, ReferenceCavePin? detail = null) =>
        CreateSurveyWorkspace(entry, detail).Project;
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
