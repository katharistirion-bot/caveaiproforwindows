using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Draws survey-detail overlays with cartographic chip styling.</summary>
public static class SurveyMapAnnotationRenderer
{
    public static void Draw(
        SurveyMapAnnotations? annotations,
        PlanCanvasDrawOptions opt,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> addElement) =>
        Draw(annotations, scene: null, project: null, opt, toScreen, pxPerMetre, darkCanvas, highContrast, addElement);

    /// <summary>Draw survey overlays with shared label layout (all 2D map tabs).</summary>
    public static void Draw(
        SurveyMapAnnotations? annotations,
        PlanScene? scene,
        CaveProjectDocument? project,
        PlanCanvasDrawOptions opt,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> addElement)
    {
        if (annotations == null)
            return;

        if (opt.ShowDepthSpanAnnotations)
            foreach (var span in annotations.DepthSpans)
                DrawDepthSpan(span, toScreen, pxPerMetre, darkCanvas, highContrast, addElement);

        if (opt.ShowBracketMarkers)
            foreach (var br in annotations.Brackets)
                DrawBracket(br, toScreen, darkCanvas, highContrast, addElement);

        if (scene == null)
        {
            var fallback = SurveyMapLabelLayout.Resolve(
                project,
                new PlanScene { Stations = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>() },
                annotations,
                opt,
                toScreen,
                pxPerMetre);
            DrawResolvedLayout(fallback, opt, toScreen, pxPerMetre, darkCanvas, highContrast, addElement);
            return;
        }

        var layout = SurveyMapLabelLayout.Resolve(project, scene, annotations, opt, toScreen, pxPerMetre);
        DrawResolvedLayout(layout, opt, toScreen, pxPerMetre, darkCanvas, highContrast, addElement);
    }

    /// <summary>Draw only annotation chips from an existing layout pass (station names drawn separately).</summary>
    public static void DrawResolvedLayout(
        SurveyMapLabelLayoutResult layout,
        PlanCanvasDrawOptions opt,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> addElement)
    {
        if (opt.ShowLegSurveyDetails)
        {
            foreach (var leg in layout.Legs)
                DrawLegLabel(leg, toScreen, pxPerMetre, darkCanvas, highContrast, addElement);
        }

        if (opt.ShowStationEnvironment)
        {
            foreach (var item in layout.Environment)
                DrawStationEnvironment(item, toScreen, darkCanvas, highContrast, addElement, opt.ShowStationNames,
                    opt.ShowStationZDepth);
        }
    }

    private static void DrawLegLabel(
        SurveyLegMapLabel leg,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> add)
    {
        var ox = (float)(leg.PerpOffsetX * pxPerMetre);
        var oy = (float)(leg.PerpOffsetY * pxPerMetre);
        var anchor = toScreen(leg.MidX, leg.MidY);
        anchor = new Point(anchor.X + ox, anchor.Y - oy);
        add(SurveyMapLabelStyle.LegChip(leg, anchor, darkCanvas, highContrast));
    }

    private static void DrawStationEnvironment(
        SurveyStationEnvMapLabel env,
        Func<float, float, Point> toScreen,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> add,
        bool stationNames,
        bool stationZ)
    {
        var pt = toScreen(env.X, env.Y);
        var yOffset = 8.0;
        if (stationNames)
            yOffset += 18;
        if (stationZ)
            yOffset += 16;
        add(SurveyMapLabelStyle.EnvChip(env.Lines, new Point(pt.X + 6, pt.Y + yOffset), darkCanvas, highContrast));
    }

    public static void DrawDepthSpan(
        SurveyDepthSpanMapLabel span,
        Func<float, float, Point> toScreen,
        double pxPerMetre,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> add)
    {
        var pal = SurveyMapLabelStyle.Palette(darkCanvas, highContrast);
        var p1 = toScreen(span.X1, span.Y1);
        var p2 = toScreen(span.X2, span.Y2);
        var stroke = new SolidColorBrush(pal.Accent) { Opacity = 0.75 };

        add(new Line
        {
            X1 = p1.X,
            Y1 = p1.Y,
            X2 = p2.X,
            Y2 = p2.Y,
            Stroke = stroke,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 3, 2.5 },
        });

        var tickLen = Math.Max(5, pxPerMetre * 0.1);
        AddPerpTick(p1, p2, tickLen, stroke, add);
        AddPerpTick(p2, p1, tickLen, stroke, add);

        var mid = new Point((p1.X + p2.X) * 0.5, (p1.Y + p2.Y) * 0.5);
        var chipLabel = span.DepthMeters > 0 ? "\u2195 " + span.Label : span.Label;
        add(SurveyMapLabelStyle.DepthSpanChip(chipLabel, new Point(mid.X - 14, mid.Y - 20), darkCanvas, highContrast));
    }

    private static void AddPerpTick(Point at, Point toward, double len, Brush stroke, Action<UIElement> add)
    {
        var dx = toward.X - at.X;
        var dy = toward.Y - at.Y;
        var mag = Math.Sqrt(dx * dx + dy * dy);
        if (mag < 1e-3)
            return;
        var nx = -dy / mag * len * 0.5;
        var ny = dx / mag * len * 0.5;
        add(new Line
        {
            X1 = at.X - nx,
            Y1 = at.Y - ny,
            X2 = at.X + nx,
            Y2 = at.Y + ny,
            Stroke = stroke,
            StrokeThickness = 1.1,
        });
    }

    public static void DrawBracket(
        SurveyBracketMapLabel br,
        Func<float, float, Point> toScreen,
        bool darkCanvas,
        bool highContrast,
        Action<UIElement> add)
    {
        add(SurveyMapLabelStyle.BracketCallout(br.Text, toScreen(br.X, br.Y), darkCanvas, highContrast));
    }

    /// <summary>SVG export — rounded label chips in survey metres (same thinning as on-screen 2D maps).</summary>
    public static void WriteSvg(
        System.Xml.XmlWriter xw,
        SurveyMapAnnotations? annotations,
        PlanScene scene,
        CaveProjectDocument project,
        PlanCanvasDrawOptions opt,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f,
        float viewBoxWidth,
        float viewBoxHeight)
    {
        if (annotations == null)
            return;

        var pxPerMetre = 960.0 / Math.Max(1.0, Math.Max(viewBoxWidth, viewBoxHeight));
        Point ToScreen(float x, float y) => new(mapX(x), mapY(y));
        var layout = SurveyMapLabelLayout.Resolve(project, scene, annotations, opt, ToScreen, pxPerMetre);

        if (opt.ShowDepthSpanAnnotations)
        {
            foreach (var span in annotations.DepthSpans)
            {
                xw.WriteStartElement("line");
                xw.WriteAttributeString("x1", f(mapX(span.X1)));
                xw.WriteAttributeString("y1", f(mapY(span.Y1)));
                xw.WriteAttributeString("x2", f(mapX(span.X2)));
                xw.WriteAttributeString("y2", f(mapY(span.Y2)));
                xw.WriteAttributeString("stroke", "#0d9488");
                xw.WriteAttributeString("stroke-width", "0.12");
                xw.WriteAttributeString("stroke-dasharray", "0.35 0.28");
                xw.WriteAttributeString("opacity", "0.85");
                xw.WriteEndElement();

                var mx = (span.X1 + span.X2) * 0.5f;
                var my = (span.Y1 + span.Y2) * 0.5f;
                var lbl = span.DepthMeters > 0 ? "\u2195 " + span.Label : span.Label;
                WriteSvgChip(xw, mx, my + 0.35f, lbl, mapX, mapY, f, 0.32f, accent: true);
            }
        }

        if (opt.ShowLegSurveyDetails)
        {
            foreach (var leg in layout.Legs)
            {
                var mx = leg.MidX + leg.PerpOffsetX;
                var my = leg.MidY + leg.PerpOffsetY + 0.45f;
                var text = leg.PrimaryLine;
                if (!string.IsNullOrEmpty(leg.SecondaryLine))
                    text += "\n" + leg.SecondaryLine;
                if (!string.IsNullOrEmpty(leg.LrudLine))
                    text += "\n" + leg.LrudLine;
                WriteSvgChip(xw, mx, my, text, mapX, mapY, f, 0.34f, accent: false);
            }
        }

        if (opt.ShowBracketMarkers)
        {
            foreach (var br in annotations.Brackets)
            {
                xw.WriteStartElement("circle");
                xw.WriteAttributeString("cx", f(mapX(br.X)));
                xw.WriteAttributeString("cy", f(mapY(br.Y)));
                xw.WriteAttributeString("r", "0.18");
                xw.WriteAttributeString("fill", "#0d9488");
                xw.WriteAttributeString("stroke", "#b8956a");
                xw.WriteAttributeString("stroke-width", "0.06");
                xw.WriteEndElement();
                WriteSvgChip(xw, br.X + 0.55f, br.Y, br.Text, mapX, mapY, f, 0.3f, accent: false);
            }
        }

        if (opt.ShowStationEnvironment)
        {
            foreach (var env in layout.Environment)
            {
                var text = string.Join("  ", env.Lines);
                WriteSvgChip(xw, env.X + 0.45f, env.Y - 0.55f, text, mapX, mapY, f, 0.28f, accent: false);
            }
        }
    }

    private static void WriteSvgChip(
        System.Xml.XmlWriter xw,
        float x,
        float y,
        string text,
        Func<float, float> mapX,
        Func<float, float> mapY,
        Func<float, string> f,
        float fontSize,
        bool accent)
    {
        var sx = mapX(x);
        var sy = mapY(y);
        var lines = text.Split('\n');
        var estW = Math.Max(1.2f, lines.Max(l => l.Length) * fontSize * 0.52f);
        var estH = lines.Length * fontSize * 1.15f + fontSize * 0.5f;

        xw.WriteStartElement("rect");
        xw.WriteAttributeString("x", f(sx - 0.15f));
        xw.WriteAttributeString("y", f(sy - estH + fontSize * 0.35f));
        xw.WriteAttributeString("width", f(estW));
        xw.WriteAttributeString("height", f(estH));
        xw.WriteAttributeString("rx", f(fontSize * 0.35f));
        xw.WriteAttributeString("fill", "#fcf8f0");
        xw.WriteAttributeString("fill-opacity", "0.94");
        xw.WriteAttributeString("stroke", accent ? "#0d9488" : "#c4a882");
        xw.WriteAttributeString("stroke-width", "0.07");
        xw.WriteEndElement();

        var lineY = sy - estH + fontSize * 0.85f;
        foreach (var line in lines)
        {
            xw.WriteStartElement("text");
            xw.WriteAttributeString("x", f(sx + 0.08f));
            xw.WriteAttributeString("y", f(lineY));
            xw.WriteAttributeString("font-size", f(fontSize));
            xw.WriteAttributeString("fill", accent && line == lines[0] ? "#0d9488" : "#1c3430");
            xw.WriteAttributeString("font-family", "Georgia, 'Palatino Linotype', serif");
            xw.WriteString(line);
            xw.WriteEndElement();
            lineY += fontSize * 1.12f;
        }
    }
}
