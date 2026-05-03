using System.Globalization;
using System.Linq;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

public static class RegistryCsvExporter
{
    public static byte[] BuildUtf8Bom(
        IReadOnlyList<CaveRegistryRow> caves,
        IReadOnlyList<BioMineralCatalogRow> catalog)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("section,cave_name,date,lat,lon,alt_m,traverse_legs,total_shots,rocks,field_catalog,library_id,cover_uri,source_file");
        foreach (var r in caves)
        {
            sb.AppendLine(string.Join(',',
                "cave",
                Csv(r.CaveName),
                Csv(r.Date),
                Csv(r.Lat),
                Csv(r.Lon),
                r.AltM.ToString(inv),
                r.TraverseLegs.ToString(inv),
                r.TotalShots.ToString(inv),
                r.RocksCount.ToString(inv),
                r.FieldCatalogCount.ToString(inv),
                Csv(r.LibraryLinkId),
                Csv(r.CoverUri),
                Csv(r.SourceFile)));
        }

        sb.AppendLine();
        sb.AppendLine("section,cave,source,category,title,species,mineral,photo_uri,coords,details");
        foreach (var r in catalog)
        {
            sb.AppendLine(string.Join(',',
                "catalog",
                Csv(r.CaveName),
                Csv(r.Source),
                Csv(r.Category),
                Csv(r.Title),
                Csv(r.Species),
                Csv(r.Mineral),
                Csv(r.PhotoUri),
                Csv(r.CoordinatesSummary),
                Csv(r.Details)));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        return preamble.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(string? x)
    {
        if (string.IsNullOrEmpty(x)) return "";
        var t = x.Replace("\"", "\"\"", StringComparison.Ordinal);
        if (t.Contains(',') || t.Contains('"') || t.Contains('\n') || t.Contains('\r'))
            return $"\"{t}\"";
        return t;
    }
}
