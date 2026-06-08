using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Export;

/// <summary>
/// KML 2.2 export for GIS viewers (Google Earth, QGIS). Uses per-station GPS when present,
/// otherwise projects local survey metres from cave entrance lat/lon.
/// </summary>
public static class SurveyKmlExporter
{
    public static void WritePlanKml(
        CaveProjectDocument project,
        TextWriter writer,
        int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(writer);

        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode)
            ?? throw new InvalidOperationException("No drawable geometry for KML.");

        var gps = SurveyStationGpsCatalog.Build(project);
        var origin = ResolveOrigin(project);
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);

        using var xml = XmlWriter.Create(writer, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
        });

        xml.WriteStartDocument();
        xml.WriteStartElement("kml", "kml", "http://www.opengis.net/kml/2.2");
        xml.WriteStartElement("Document");
        xml.WriteElementString("name", Escape(project.Name));

        WriteStyle(xml, "traverse", "ff00ddff", 4);
        WriteStyle(xml, "station", "ff38bdf8", 6);

        // Traverse centreline
        xml.WriteStartElement("Placemark");
        xml.WriteElementString("name", $"{project.Name} centreline");
        xml.WriteElementString("styleUrl", "#traverse");
        xml.WriteStartElement("LineString");
        xml.WriteElementString("tessellate", "1");
        xml.WriteStartElement("coordinates");
        var sb = new StringBuilder();
        foreach (var seg in scene.TraverseSegments)
        {
            AppendCoord(sb, seg.x1, seg.y1, coords, gps, origin);
            AppendCoord(sb, seg.x2, seg.y2, coords, gps, origin);
        }
        xml.WriteString(sb.ToString().Trim());
        xml.WriteEndElement(); // coordinates
        xml.WriteEndElement(); // LineString
        xml.WriteEndElement(); // Placemark

        foreach (var st in coords.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var (lat, lon, alt) = ToWgs84(st.X, st.Y, st.Z, st.Name, gps, origin);
            xml.WriteStartElement("Placemark");
            xml.WriteElementString("name", st.Name);
            xml.WriteElementString("styleUrl", "#station");
            xml.WriteStartElement("Point");
            xml.WriteElementString("coordinates",
                $"{lon.ToString(CultureInfo.InvariantCulture)},{lat.ToString(CultureInfo.InvariantCulture)},{alt.ToString(CultureInfo.InvariantCulture)}");
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        xml.WriteEndElement(); // Document
        xml.WriteEndElement(); // kml
        xml.WriteEndDocument();
    }

    private static void WriteStyle(XmlWriter xml, string id, string abgrColor, int width)
    {
        xml.WriteStartElement("Style");
        xml.WriteAttributeString("id", id);
        xml.WriteStartElement("LineStyle");
        xml.WriteElementString("color", abgrColor);
        xml.WriteElementString("width", width.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
        xml.WriteStartElement("IconStyle");
        xml.WriteElementString("scale", "0.7");
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static (double lat, double lon, double alt) ResolveOrigin(CaveProjectDocument project)
    {
        if (project.Lat is { } la && project.Lon is { } lo)
            return (la, lo, project.Alt);
        return (0, 0, 0);
    }

    private static (double lat, double lon, double alt) ToWgs84(
        float x,
        float y,
        float z,
        string station,
        IReadOnlyDictionary<string, SurveyStationGpsFix> gps,
        (double lat, double lon, double alt) origin)
    {
        if (gps.TryGetValue(station, out var fix))
            return (fix.Lat, fix.Lon, z);

        if (Math.Abs(origin.lat) < 1e-9 && Math.Abs(origin.lon) < 1e-9)
            return (y / 111_320.0, x / 111_320.0, z);

        var lat = origin.lat + y / 111_320.0;
        var lon = origin.lon + x / (111_320.0 * Math.Cos(origin.lat * Math.PI / 180.0));
        return (lat, lon, origin.alt + z);
    }

    private static void AppendCoord(
        StringBuilder sb,
        float x,
        float y,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> coords,
        IReadOnlyDictionary<string, SurveyStationGpsFix> gps,
        (double lat, double lon, double alt) origin)
    {
        var st = coords.Values.FirstOrDefault(c => Math.Abs(c.X - x) < 0.01 && Math.Abs(c.Y - y) < 0.01);
        var name = st?.Name ?? "";
        var z = st?.Z ?? 0;
        var (lat, lon, alt) = ToWgs84(x, y, z, name, gps, origin);
        sb.Append(lon.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(lat.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(alt.ToString(CultureInfo.InvariantCulture)).Append(' ');
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
