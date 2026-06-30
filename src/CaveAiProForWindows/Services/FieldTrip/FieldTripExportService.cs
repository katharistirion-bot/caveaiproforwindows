using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Services.FieldTrip;

public static class FieldTripExportService
{
    public static string BuildTextSummary(FieldTripDocument trip)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CAVE AI PRO — Field trip itinerary");
        sb.AppendLine(trip.Name);
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrWhiteSpace(trip.Notes))
        {
            sb.AppendLine();
            sb.AppendLine(trip.Notes.Trim());
        }

        sb.AppendLine();
        var i = 1;
        foreach (var stop in trip.Stops)
        {
            sb.AppendLine($"{i}. {stop.Name}");
            sb.AppendLine($"   {stop.Lat:F5}, {stop.Lon:F5}");
            if (!string.IsNullOrWhiteSpace(stop.Country))
                sb.AppendLine($"   {stop.Country}");
            if (stop.DepthM is > 0 || stop.LengthM is > 0)
                sb.AppendLine($"   depth {stop.DepthM:0.#} m · length {stop.LengthM:0.#} m");
            if (!string.IsNullOrWhiteSpace(stop.Notes))
                sb.AppendLine($"   {stop.Notes.Trim()}");
            sb.AppendLine();
            i++;
        }

        return sb.ToString().TrimEnd();
    }

    public static string ExportGpx(FieldTripDocument trip)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine($@"<gpx version=""1.1"" creator=""CAVE AI PRO"" xmlns=""http://www.topografix.com/GPX/1/1"">");
        sb.AppendLine($"  <metadata><name>{EscapeXml(trip.Name)}</name></metadata>");
        sb.AppendLine(@"  <rte>");
        sb.AppendLine($"    <name>{EscapeXml(trip.Name)}</name>");
        var seq = 1;
        foreach (var stop in trip.Stops)
        {
            sb.AppendLine("    <rtept");
            sb.AppendLine($"      lat=\"{stop.Lat.ToString(inv)}\" lon=\"{stop.Lon.ToString(inv)}\">");
            sb.AppendLine($"      <name>{EscapeXml(stop.Name)}</name>");
            if (!string.IsNullOrWhiteSpace(stop.Notes))
                sb.AppendLine($"      <desc>{EscapeXml(stop.Notes)}</desc>");
            sb.AppendLine($"      <type>Stop {seq}</type>");
            sb.AppendLine("    </rtept>");
            seq++;
        }

        sb.AppendLine(@"  </rte>");
        sb.AppendLine(@"</gpx>");
        return sb.ToString();
    }

    public static string ExportKml(FieldTripDocument trip)
    {
        var inv = CultureInfo.InvariantCulture;
        var coords = string.Join(" ",
            trip.Stops.Select(s => $"{s.Lon.ToString(inv)},{s.Lat.ToString(inv)},0"));

        var doc = new XDocument(
            new XElement("{http://www.opengis.net/kml/2.2}kml",
                new XElement("{http://www.opengis.net/kml/2.2}Document",
                    new XElement("{http://www.opengis.net/kml/2.2}name", trip.Name),
                    new XElement("{http://www.opengis.net/kml/2.2}Placemark",
                        new XElement("{http://www.opengis.net/kml/2.2}name", trip.Name),
                        new XElement("{http://www.opengis.net/kml/2.2}LineString",
                            new XElement("{http://www.opengis.net/kml/2.2}coordinates", coords))))));

        return doc.ToString();
    }

    public static FieldTripStop FromIndexEntry(ReferenceCaveIndexEntry entry) =>
        new()
        {
            ReferenceId = entry.Id,
            Name = entry.Name,
            Lat = entry.Lat,
            Lon = entry.Lon,
            Country = entry.Country,
            DepthM = entry.DepthM,
            LengthM = entry.LengthM,
        };

    /// <summary>Google Maps directions URL (origin → waypoints → destination).</summary>
    public static string? BuildGoogleMapsDirectionsUrl(FieldTripDocument trip)
    {
        if (trip.Stops.Count < 2)
            return null;

        var inv = CultureInfo.InvariantCulture;
        string Fmt(FieldTripStop s) => $"{s.Lat.ToString(inv)},{s.Lon.ToString(inv)}";
        var origin = Fmt(trip.Stops[0]);
        var destination = Fmt(trip.Stops[^1]);
        var waypoints = trip.Stops.Count > 2
            ? string.Join("|", trip.Stops.Skip(1).Take(trip.Stops.Count - 2).Select(Fmt))
            : null;

        var url = $"https://www.google.com/maps/dir/?api=1&travelmode=driving&origin={Uri.EscapeDataString(origin)}&destination={Uri.EscapeDataString(destination)}";
        if (!string.IsNullOrEmpty(waypoints))
            url += "&waypoints=" + Uri.EscapeDataString(waypoints);
        return url;
    }

    private static string EscapeXml(string? text) =>
        System.Security.SecurityElement.Escape(text ?? "") ?? "";
}

public static class FieldTripMapBridge
{
    public const string VirtualHost = "caveai-fieldtrip.local";
    public static string EntryUri => $"https://{VirtualHost}/index.html";

    public static string BuildStopsMessageJson(IReadOnlyList<FieldTripStop> stops) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "stops",
            payload = new
            {
                stops = stops.Where(s => s.Lat != 0 || s.Lon != 0)
                    .Select(s => new { name = s.Name, lat = s.Lat, lon = s.Lon }).ToList(),
            },
        });
}
