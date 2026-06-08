using System.Globalization;
using System.Xml;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services;

/// <summary>Scale bar, north arrow, legend, and metadata for SVG map export.</summary>
internal static class CaveMappingSvgCartography
{
    public static void WriteSurveyOverlays(
        XmlWriter xw,
        PlanScene scene,
        PlanCanvasDrawOptions opt,
        float vbW,
        float vbH,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f)
    {
        if (!opt.ShowCoordinateGrid && !opt.ShowLoopClosureHighlights && !opt.ShowLrudQcHighlights)
            return;

        xw.WriteStartElement("g");
        xw.WriteAttributeString("id", "caveai-survey-overlays");

        if (opt.ShowCoordinateGrid)
            WriteCoordinateGrid(xw, scene, vbW, vbH, mapX, mapY, f);

        if (opt.ShowLoopClosureHighlights)
            WriteLoopClosureHighlights(xw, scene, mapX, mapY, f);

        if (opt.ShowLrudQcHighlights)
            WriteLrudQcHighlights(xw, scene, mapX, mapY, f);

        xw.WriteEndElement();
    }

    public static void WriteOverlay(
        XmlWriter xw,
        PlanScene scene,
        PlanCanvasDrawOptions opt,
        float vbW,
        float vbH,
        Func<float, string> f)
    {
        if (!opt.ShowCartographyOverlay && !opt.ShowSymbolLegend && opt.ExportMetadata == null)
            return;

        xw.WriteStartElement("g");
        xw.WriteAttributeString("id", "caveai-cartography");

        if (opt.ShowCartographyOverlay)
        {
            WriteScaleBar(xw, vbW, vbH, f);
            WriteNorthArrow(xw, vbW, vbH, f);
        }

        if (opt.ShowSymbolLegend)
            WriteSymbolLegend(xw, vbW, f);

        if (opt.ExportMetadata != null)
            WriteMetadataFooter(xw, opt.ExportMetadata, vbW, vbH, f);

        xw.WriteEndElement();
    }

    private static void WriteScaleBar(XmlWriter xw, float vbW, float vbH, Func<float, string> f)
    {
        var spanM = Math.Max(1f, vbW * 0.92f);
        var barM = NiceScaleBarMetres(spanM * 0.18f);
        var barLen = barM;
        var x = 0.8f;
        var y = vbH - 1.1f;

        xw.WriteStartElement("g");
        xw.WriteAttributeString("transform", $"translate({f(x)},{f(y)})");

        xw.WriteStartElement("rect");
        xw.WriteAttributeString("x", "0");
        xw.WriteAttributeString("y", "-0.55");
        xw.WriteAttributeString("width", f(barLen + 1.6f));
        xw.WriteAttributeString("height", "1.0");
        xw.WriteAttributeString("rx", "0.15");
        xw.WriteAttributeString("fill", "#ffffff");
        xw.WriteAttributeString("fill-opacity", "0.92");
        xw.WriteAttributeString("stroke", "#bec4c8");
        xw.WriteEndElement();

        xw.WriteStartElement("line");
        xw.WriteAttributeString("x1", f(0.35f));
        xw.WriteAttributeString("y1", f(-0.05f));
        xw.WriteAttributeString("x2", f(0.35f + barLen));
        xw.WriteAttributeString("y2", f(-0.05f));
        xw.WriteAttributeString("stroke", "#263238");
        xw.WriteAttributeString("stroke-width", "0.12");
        xw.WriteEndElement();

        xw.WriteStartElement("text");
        xw.WriteAttributeString("x", f(0.35f + barLen + 0.25f));
        xw.WriteAttributeString("y", f(0.05f));
        xw.WriteAttributeString("font-size", f(0.38f));
        xw.WriteAttributeString("font-weight", "600");
        xw.WriteAttributeString("fill", "#263238");
        xw.WriteAttributeString("font-family", "Segoe UI, sans-serif");
        xw.WriteString($"0 — {barM:0.##} m");
        xw.WriteEndElement();

        xw.WriteEndElement();
    }

    private static void WriteNorthArrow(XmlWriter xw, float vbW, float vbH, Func<float, string> f)
    {
        var cx = vbW - 2.2f;
        var cy = vbH - 2.2f;
        var r = 0.85f;

        xw.WriteStartElement("g");
        xw.WriteAttributeString("transform", $"translate({f(cx)},{f(cy)})");

        xw.WriteStartElement("rect");
        xw.WriteAttributeString("x", f(-r - 0.35f));
        xw.WriteAttributeString("y", f(-r - 0.35f));
        xw.WriteAttributeString("width", f(2 * r + 0.7f));
        xw.WriteAttributeString("height", f(2 * r + 0.7f));
        xw.WriteAttributeString("rx", "0.2");
        xw.WriteAttributeString("fill", "#ffffff");
        xw.WriteAttributeString("fill-opacity", "0.92");
        xw.WriteAttributeString("stroke", "#bec4c8");
        xw.WriteEndElement();

        xw.WriteStartElement("circle");
        xw.WriteAttributeString("cx", "0");
        xw.WriteAttributeString("cy", "0");
        xw.WriteAttributeString("r", f(r));
        xw.WriteAttributeString("fill", "none");
        xw.WriteAttributeString("stroke", "#263238");
        xw.WriteAttributeString("stroke-width", "0.08");
        xw.WriteEndElement();

        xw.WriteStartElement("polygon");
        xw.WriteAttributeString("points",
            $"0,{f((float)(-r * 0.72))} {f((float)(-r * 0.22))},{f((float)(r * 0.18))} {f((float)(r * 0.22))},{f((float)(r * 0.18))}");
        xw.WriteAttributeString("fill", "#263238");
        xw.WriteEndElement();

        xw.WriteStartElement("text");
        xw.WriteAttributeString("x", "0");
        xw.WriteAttributeString("y", f(-r - 0.05f));
        xw.WriteAttributeString("text-anchor", "middle");
        xw.WriteAttributeString("font-size", f(0.42f));
        xw.WriteAttributeString("font-weight", "bold");
        xw.WriteAttributeString("fill", "#263238");
        xw.WriteString("N");
        xw.WriteEndElement();

        xw.WriteEndElement();
    }

    private static void WriteSymbolLegend(XmlWriter xw, float vbW, Func<float, string> f)
    {
        var kinds = CaveMappingSymbolCatalog.DefaultLegendKinds;

        var boxW = 5.2f;
        var boxH = 0.55f + kinds.Count * 0.52f;
        var x = vbW - boxW - 0.5f;
        var y = 0.45f;

        xw.WriteStartElement("g");
        xw.WriteAttributeString("transform", $"translate({f(x)},{f(y)})");

        xw.WriteStartElement("rect");
        xw.WriteAttributeString("width", f(boxW));
        xw.WriteAttributeString("height", f(boxH));
        xw.WriteAttributeString("rx", "0.18");
        xw.WriteAttributeString("fill", "#ffffff");
        xw.WriteAttributeString("fill-opacity", "0.92");
        xw.WriteAttributeString("stroke", "#bec4c8");
        xw.WriteEndElement();

        xw.WriteStartElement("text");
        xw.WriteAttributeString("x", f(0.25f));
        xw.WriteAttributeString("y", f(0.42f));
        xw.WriteAttributeString("font-size", f(0.36f));
        xw.WriteAttributeString("font-weight", "600");
        xw.WriteAttributeString("fill", "#263238");
        xw.WriteString("Symbol legend (UICC-style)");
        xw.WriteEndElement();

        for (var i = 0; i < kinds.Count; i++)
        {
            var rowY = 0.75f + i * 0.52f;
            var kind = kinds[i];
            xw.WriteStartElement("g");
            xw.WriteAttributeString("transform", $"translate({f(0.35f)},{f(rowY)}) scale({f(0.42f)}) translate(-0.5,-0.5)");
            var geom = CaveMappingSymbolCatalog.GetGeometry(kind);
            var pathD = GeometryToUnitPath(geom);
            if (!string.IsNullOrEmpty(pathD))
            {
                xw.WriteStartElement("path");
                xw.WriteAttributeString("d", pathD);
                xw.WriteAttributeString("fill", "#6645a89a");
                xw.WriteAttributeString("stroke", "#263238");
                xw.WriteAttributeString("stroke-width", "0.06");
                xw.WriteEndElement();
            }

            xw.WriteEndElement();

            xw.WriteStartElement("text");
            xw.WriteAttributeString("x", f(0.85f));
            xw.WriteAttributeString("y", f(rowY + 0.05f));
            xw.WriteAttributeString("font-size", f(0.32f));
            xw.WriteAttributeString("fill", "#263238");
            xw.WriteString(CaveMappingSymbolCatalog.GetLabel(kind));
            xw.WriteEndElement();
        }

        xw.WriteEndElement();
    }

    private static void WriteMetadataFooter(
        XmlWriter xw,
        CaveMappingExportMetadata metadata,
        float vbW,
        float vbH,
        Func<float, string> f)
    {
        var line = metadata.BuildSubtitleLine();
        if (string.IsNullOrWhiteSpace(line))
            return;

        xw.WriteStartElement("text");
        xw.WriteAttributeString("x", f(0.5f));
        xw.WriteAttributeString("y", f(vbH - 0.35f));
        xw.WriteAttributeString("font-size", f(0.34f));
        xw.WriteAttributeString("font-style", "italic");
        xw.WriteAttributeString("fill", "#546e7a");
        xw.WriteAttributeString("font-family", "Segoe UI, sans-serif");
        xw.WriteString(line);
        xw.WriteEndElement();
    }

    private static void WriteCoordinateGrid(
        XmlWriter xw,
        PlanScene scene,
        float vbW,
        float vbH,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f)
    {
        var pxPerMetre = vbW / Math.Max(scene.SpanX, 1e-6f);
        var spacing = (float)SurveyCoordinateGridRenderer.ResolveGridSpacingMetres(pxPerMetre, Math.Max(scene.SpanX, scene.SpanY));
        var minX = scene.MinX;
        var maxX = scene.MaxX;
        var minY = scene.MinY;
        var maxY = scene.MaxY;
        var startX = (float)(Math.Floor(minX / spacing) * spacing);
        var startY = (float)(Math.Floor(minY / spacing) * spacing);

        for (var x = startX; x <= maxX + spacing * 0.5f; x += spacing)
        {
            xw.WriteStartElement("line");
            xw.WriteAttributeString("x1", f(mapX(x)));
            xw.WriteAttributeString("y1", f(mapY(minY)));
            xw.WriteAttributeString("x2", f(mapX(x)));
            xw.WriteAttributeString("y2", f(mapY(maxY)));
            xw.WriteAttributeString("stroke", "#9E9688");
            xw.WriteAttributeString("stroke-opacity", "0.35");
            xw.WriteAttributeString("stroke-width", "0.06");
            xw.WriteEndElement();
        }

        for (var y = startY; y <= maxY + spacing * 0.5f; y += spacing)
        {
            xw.WriteStartElement("line");
            xw.WriteAttributeString("x1", f(mapX(minX)));
            xw.WriteAttributeString("y1", f(mapY(y)));
            xw.WriteAttributeString("x2", f(mapX(maxX)));
            xw.WriteAttributeString("y2", f(mapY(y)));
            xw.WriteAttributeString("stroke", "#9E9688");
            xw.WriteAttributeString("stroke-opacity", "0.35");
            xw.WriteAttributeString("stroke-width", "0.06");
            xw.WriteEndElement();
        }
    }

    private static void WriteLoopClosureHighlights(
        XmlWriter xw,
        PlanScene scene,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f)
    {
        foreach (var loop in scene.LoopClosingLegs)
        {
            if (!scene.Stations.TryGetValue(loop.FromStation, out var a) ||
                !scene.Stations.TryGetValue(loop.ToStation, out var b))
                continue;

            var stroke = loop.Severity switch
            {
                LoopClosureSeverity.Good => "#16a34a",
                LoopClosureSeverity.Moderate => "#d97706",
                _ => "#dc2626",
            };

            xw.WriteStartElement("line");
            xw.WriteAttributeString("x1", f(mapX(a.X)));
            xw.WriteAttributeString("y1", f(mapY(a.Y)));
            xw.WriteAttributeString("x2", f(mapX(b.X)));
            xw.WriteAttributeString("y2", f(mapY(b.Y)));
            xw.WriteAttributeString("stroke", stroke);
            xw.WriteAttributeString("stroke-width", "0.22");
            xw.WriteAttributeString("stroke-dasharray", "0.35 0.22");
            xw.WriteEndElement();
        }
    }

    private static void WriteLrudQcHighlights(
        XmlWriter xw,
        PlanScene scene,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f)
    {
        foreach (var issue in scene.LrudQcHighlights)
        {
            if (string.IsNullOrEmpty(issue.FromStation) ||
                string.IsNullOrEmpty(issue.ToStation) ||
                !scene.Stations.TryGetValue(issue.FromStation, out var qa) ||
                !scene.Stations.TryGetValue(issue.ToStation, out var qb))
                continue;

            xw.WriteStartElement("line");
            xw.WriteAttributeString("x1", f(mapX(qa.X)));
            xw.WriteAttributeString("y1", f(mapY(qa.Y)));
            xw.WriteAttributeString("x2", f(mapX(qb.X)));
            xw.WriteAttributeString("y2", f(mapY(qb.Y)));
            xw.WriteAttributeString("stroke", "#c026d3");
            xw.WriteAttributeString("stroke-width", "0.18");
            xw.WriteAttributeString("stroke-dasharray", "0.25 0.18");
            xw.WriteEndElement();
        }
    }

    private static float NiceScaleBarMetres(float rawMetres)
    {
        if (rawMetres <= 0)
            return 1;
        var p = (float)Math.Pow(10, Math.Floor(Math.Log10(rawMetres)));
        var m = rawMetres / p;
        if (m <= 1.5f) return p;
        if (m <= 3.5f) return 2 * p;
        if (m <= 7.5f) return 5 * p;
        return 10 * p;
    }

    private static string GeometryToUnitPath(System.Windows.Media.Geometry geometry)
    {
        var sb = new System.Text.StringBuilder();
        if (geometry is System.Windows.Media.PathGeometry pg)
        {
            foreach (var fig in pg.Figures)
            {
                sb.Append('M').Append(fig.StartPoint.X.ToString(CultureInfo.InvariantCulture))
                    .Append(' ').Append(fig.StartPoint.Y.ToString(CultureInfo.InvariantCulture));
                foreach (var seg in fig.Segments)
                {
                    if (seg is System.Windows.Media.LineSegment ls)
                        sb.Append('L').Append(ls.Point.X.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(ls.Point.Y.ToString(CultureInfo.InvariantCulture));
                    else if (seg is System.Windows.Media.ArcSegment arc)
                        sb.Append('L').Append(arc.Point.X.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(arc.Point.Y.ToString(CultureInfo.InvariantCulture));
                    else if (seg is System.Windows.Media.QuadraticBezierSegment qb)
                        sb.Append('Q').Append(qb.Point1.X.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(qb.Point1.Y.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(qb.Point2.X.ToString(CultureInfo.InvariantCulture))
                            .Append(' ').Append(qb.Point2.Y.ToString(CultureInfo.InvariantCulture));
                }

                if (fig.IsClosed)
                    sb.Append('Z');
            }
        }

        return sb.ToString();
    }
}
