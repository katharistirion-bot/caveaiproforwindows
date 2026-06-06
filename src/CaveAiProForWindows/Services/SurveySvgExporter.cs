using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Minimal SVG export (traverse + walls + vectors + stations + map symbols) in survey metres.</summary>
public static class SurveySvgExporter
{
    public static void WritePlanSvg(
        CaveProjectDocument project,
        Stream stream,
        int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan,
        SurveyVisualizationMode visualization = SurveyVisualizationMode.Standard,
        PlanCanvasDrawOptions? drawOptions = null)
    {
        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode, visualization)
            ?? throw new InvalidOperationException("No drawable geometry for SVG.");
        var opt = drawOptions ?? new PlanCanvasDrawOptions(
            ShowStationNames: true,
            ShowStationZDepth: vectorViewMode == SurveyStationGeometry.AndroidViewModePlan,
            CanvasKind: vectorViewMode == SurveyStationGeometry.AndroidViewModeSection
                ? SurveyCanvasKind.Section
                : SurveyCanvasKind.Plan,
            VisualizationMode: visualization,
            ShowLegSurveyDetails: true,
            ShowStationEnvironment: true,
            ShowDepthSpanAnnotations: true,
            ShowBracketMarkers: true);
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

        var displayName = CaveProjectDisplayNames.GetDisplayName(project);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var viewLabel = vectorViewMode == SurveyStationGeometry.AndroidViewModeSection ? "Section" : "Plan";
            if (!string.IsNullOrWhiteSpace(project.Date))
                viewLabel += " · " + project.Date.Trim();
            WriteSvgCaveTitle(xw, displayName, viewLabel, F, vbW);
        }

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
            WriteSymbol(xw, sym, MapX, MapY, F);

        var annotations = SurveyMapAnnotationsBuilder.Build(
            project,
            scene,
            opt.AnnotationViewMode,
            opt.ShowLegSurveyDetails,
            opt.ShowStationEnvironment);
        SurveyMapAnnotationRenderer.WriteSvg(xw, annotations, scene, project, opt, MapX, MapY, F, vbW, vbH);

        xw.WriteEndElement();
        xw.WriteEndDocument();
    }

    private static void WriteSymbol(
        XmlWriter xw,
        SurveyStationGeometry.PlanMapSymbol sym,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f)
    {
        var cx = mapX(sym.X);
        var cy = mapY(sym.Y);
        var spanM = sym.ScaleSurveyMetres ??
                    (float)(SketchSymbolDefinitions.DefaultSymbolWorldSpanMetres * Math.Max(0.12, sym.Scale));
        spanM = Math.Clamp(spanM, 0.08f, 80f);
        var resolved = MapSymbolIconResolver.Resolve(sym, highContrast: false);
        var label = MapSymbolIconResolver.ExportLabel(sym);

        xw.WriteStartElement("g");
        xw.WriteAttributeString("transform",
            $"translate({f(cx)},{f(cy)}) rotate({f(sym.RotationDegrees)})");
        xw.WriteStartElement("title");
        xw.WriteString(label);
        xw.WriteEndElement();

        if (resolved.Mode == MapSymbolIconResolver.RenderMode.EmojiGlyph && !string.IsNullOrEmpty(resolved.Emoji))
        {
            xw.WriteStartElement("text");
            xw.WriteAttributeString("text-anchor", "middle");
            xw.WriteAttributeString("dominant-baseline", "central");
            xw.WriteAttributeString("font-size", f(spanM * 0.85f));
            xw.WriteString(resolved.Emoji);
            xw.WriteEndElement();
            xw.WriteEndElement();
            return;
        }

        if (resolved.Geometry == null)
        {
            xw.WriteEndElement();
            return;
        }

        var scale = resolved.NormalizedUisSpace
            ? spanM * 0.5f
            : spanM / (float)SketchSymbolDefinitions.ViewboxNominalSize;
        var pathD = GeometryToPathData(resolved.Geometry, scale, resolved.NormalizedUisSpace);
        if (string.IsNullOrEmpty(pathD))
        {
            xw.WriteEndElement();
            return;
        }

        xw.WriteStartElement("path");
        xw.WriteAttributeString("d", pathD);
        xw.WriteAttributeString("fill", BrushToSvg(resolved.Fill));
        xw.WriteAttributeString("stroke", BrushToSvg(resolved.Stroke));
        xw.WriteAttributeString("stroke-width", f(Math.Max(0.08f, spanM * 0.06f)));
        xw.WriteAttributeString("stroke-linejoin", "round");
        xw.WriteAttributeString("stroke-linecap", "round");
        xw.WriteEndElement();
        xw.WriteEndElement();
    }

    private static string GeometryToPathData(Geometry geometry, float scale, bool normalizedUis)
    {
        var sb = new StringBuilder();
        var offset = normalizedUis ? 0.0 : SketchSymbolDefinitions.ViewboxNominalSize * 0.5;
        void AppendFigure(PathFigure fig)
        {
            var start = fig.StartPoint;
            var sx = (float)((start.X - offset) * scale);
            var sy = (float)((start.Y - offset) * scale);
            sb.Append('M').Append(sx.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(sy.ToString(CultureInfo.InvariantCulture));
            foreach (var seg in fig.Segments)
            {
                if (seg is LineSegment ls)
                {
                    var p = ls.Point;
                    sb.Append('L').Append(((float)((p.X - offset) * scale)).ToString(CultureInfo.InvariantCulture))
                        .Append(' ').Append(((float)((p.Y - offset) * scale)).ToString(CultureInfo.InvariantCulture));
                }
            }

            if (fig.IsClosed)
                sb.Append('Z');
        }

        void Walk(Geometry g)
        {
            switch (g)
            {
                case GeometryGroup grp:
                    foreach (var child in grp.Children)
                        Walk(child);
                    break;
                case PathGeometry pg:
                    foreach (var fig in pg.Figures)
                        AppendFigure(fig);
                    break;
                case LineGeometry lg:
                {
                    var fig = new PathFigure(lg.StartPoint, [new LineSegment(lg.EndPoint, true)], false);
                    AppendFigure(fig);
                    break;
                }
            }
        }

        Walk(geometry);
        return sb.ToString();
    }

    private static string BrushToSvg(Brush? brush)
    {
        if (brush is SolidColorBrush sc)
        {
            var c = sc.Color;
            if (c.A < 255)
                return $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString(CultureInfo.InvariantCulture)})";
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        return "#263238";
    }

    private static void WriteSvgCaveTitle(XmlWriter xw, string caveName, string subtitle, Func<float, string> f, float vbW)
    {
        var estW = Math.Min(vbW * 0.55f, Math.Max(3f, caveName.Length * 0.38f + 1.2f));
        var estH = 1.35f;

        xw.WriteStartElement("rect");
        xw.WriteAttributeString("x", f(0.45f));
        xw.WriteAttributeString("y", f(0.35f));
        xw.WriteAttributeString("width", f(estW));
        xw.WriteAttributeString("height", f(estH));
        xw.WriteAttributeString("rx", f(0.22f));
        xw.WriteAttributeString("fill", "#fcf8f0");
        xw.WriteAttributeString("fill-opacity", "0.95");
        xw.WriteAttributeString("stroke", "#0d9488");
        xw.WriteAttributeString("stroke-width", "0.08");
        xw.WriteEndElement();

        xw.WriteStartElement("text");
        xw.WriteAttributeString("x", f(0.65f));
        xw.WriteAttributeString("y", f(0.95f));
        xw.WriteAttributeString("font-size", f(0.55f));
        xw.WriteAttributeString("font-weight", "bold");
        xw.WriteAttributeString("fill", "#1c3430");
        xw.WriteAttributeString("font-family", "Georgia, 'Palatino Linotype', serif");
        xw.WriteString(caveName);
        xw.WriteEndElement();

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            xw.WriteStartElement("text");
            xw.WriteAttributeString("x", f(0.65f));
            xw.WriteAttributeString("y", f(1.18f));
            xw.WriteAttributeString("font-size", f(0.32f));
            xw.WriteAttributeString("fill", "#0d9488");
            xw.WriteAttributeString("font-family", "Consolas, monospace");
            xw.WriteString(subtitle);
            xw.WriteEndElement();
        }
    }
}
