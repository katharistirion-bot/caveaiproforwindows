using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Minimal SVG export (traverse + walls + vectors + stations) in survey metres.</summary>
public static class SurveySvgExporter
{
    public static void WritePlanSvg(
        CaveProjectDocument project,
        Stream stream,
        int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard)
    {
        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode, visualization)
            ?? throw new InvalidOperationException("No drawable geometry for SVG.");
        using var xw = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        var pad = 4f;
        var vbW = scene.SpanX + 2 * pad;
        var vbH = scene.SpanY + 2 * pad;
        var ox = scene.MinX - pad;
        var oy = scene.MinY - pad;

        static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        float MapX(float x) => x - ox;
        float MapY(float y) => scene.MaxY + pad - (y - oy);

        xw.WriteStartDocument();
        xw.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        xw.WriteAttributeString("xmlns", "http://www.w3.org/2000/svg");
        xw.WriteAttributeString("viewBox", $"0 0 {F(vbW)} {F(vbH)}");
        xw.WriteAttributeString("width", F(vbW));
        xw.WriteAttributeString("height", F(vbH));

        xw.WriteStartElement("title");
        xw.WriteString($"{project.Name} — {(vectorViewMode == SurveyStationGeometry.AndroidViewModePlan ? "Plan" : "Section vectors")} (m)");
        xw.WriteEndElement();

        void Polyline(string stroke, string? fill, double width, IReadOnlyList<(float x, float y)> pts, bool closed)
        {
            if (pts.Count < 2)
                return;
            var sb = new StringBuilder();
            foreach (var (x, y) in pts)
                sb.Append(F(MapX(x))).Append(',').Append(F(MapY(y))).Append(' ');
            xw.WriteStartElement(closed ? "polygon" : "polyline");
            xw.WriteAttributeString("points", sb.ToString().TrimEnd());
            xw.WriteAttributeString("fill", fill ?? "none");
            xw.WriteAttributeString("stroke", stroke);
            xw.WriteAttributeString("stroke-width", F((float)width));
            xw.WriteAttributeString("stroke-linejoin", "round");
            xw.WriteEndElement();
        }

        foreach (var pl in scene.WallPolylines)
        {
            if (pl.Closed && pl.Points.Count >= 3)
                Polyline("#d97706", "#fed7aa55", 1.8f, pl.Points, true);
            else if (pl.Points.Count >= 2)
                Polyline("#d97706", null, 2f, pl.Points, false);
        }

        foreach (var pl in scene.VectorPolylines)
        {
            if (pl.Closed && pl.Points.Count >= 3)
                Polyline("#0284c7", "#38bdf833", 1.6f, pl.Points, true);
            else if (pl.Points.Count >= 2)
                Polyline("#0ea5e9", null, 1.6f, pl.Points, false);
        }

        foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
        {
            xw.WriteStartElement("line");
            xw.WriteAttributeString("x1", F(MapX(x1)));
            xw.WriteAttributeString("y1", F(MapY(y1)));
            xw.WriteAttributeString("x2", F(MapX(x2)));
            xw.WriteAttributeString("y2", F(MapY(y2)));
            xw.WriteAttributeString("stroke", "#16a34a");
            xw.WriteAttributeString("stroke-width", "2.5");
            xw.WriteAttributeString("stroke-linecap", "round");
            xw.WriteEndElement();
        }

        foreach (var kv in scene.Stations)
        {
            var c = kv.Value;
            xw.WriteStartElement("circle");
            xw.WriteAttributeString("cx", F(MapX(c.X)));
            xw.WriteAttributeString("cy", F(MapY(c.Y)));
            xw.WriteAttributeString("r", "3.5");
            xw.WriteAttributeString("fill", "#facc15");
            xw.WriteAttributeString("stroke", "#166534");
            xw.WriteAttributeString("stroke-width", "1");
            xw.WriteEndElement();
        }

        foreach (var sym in scene.Symbols)
        {
            xw.WriteStartElement("circle");
            xw.WriteAttributeString("cx", F(MapX(sym.X)));
            xw.WriteAttributeString("cy", F(MapY(sym.Y)));
            xw.WriteAttributeString("r", "4");
            xw.WriteAttributeString("fill", "#c026d3");
            xw.WriteAttributeString("stroke", "#6d28d9");
            xw.WriteAttributeString("stroke-width", "1");
            xw.WriteEndElement();
        }

        xw.WriteEndElement();
        xw.WriteEndDocument();
    }
}
