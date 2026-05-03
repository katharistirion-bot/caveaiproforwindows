using System.Globalization;
using System.IO;
using System.Text;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Minimal ASCII DXF (R12-style) — LINE entities in survey metres.</summary>
public static class SurveyDxfExporter
{
    public static void WritePlanDxf(CaveProjectDocument project, TextWriter w, int vectorViewMode = SurveyStationGeometry.AndroidViewModePlan)
    {
        var scene = PlanSceneBuilder.TryBuild(project, vectorViewMode)
            ?? throw new InvalidOperationException("No drawable geometry for DXF.");

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        w.WriteLine("0");
        w.WriteLine("SECTION");
        w.WriteLine("2");
        w.WriteLine("HEADER");
        w.WriteLine("9");
        w.WriteLine("$ACADVER");
        w.WriteLine("1");
        w.WriteLine("AC1012");
        w.WriteLine("0");
        w.WriteLine("ENDSEC");
        w.WriteLine("0");
        w.WriteLine("SECTION");
        w.WriteLine("2");
        w.WriteLine("ENTITIES");

        void EmitDxfLine(string layer, double x1, double y1, double x2, double y2)
        {
            w.WriteLine("0");
            w.WriteLine("LINE");
            w.WriteLine("8");
            w.WriteLine(layer);
            w.WriteLine("10");
            w.WriteLine(F(x1));
            w.WriteLine("20");
            w.WriteLine(F(y1));
            w.WriteLine("30");
            w.WriteLine("0.0");
            w.WriteLine("11");
            w.WriteLine(F(x2));
            w.WriteLine("21");
            w.WriteLine(F(y2));
            w.WriteLine("31");
            w.WriteLine("0.0");
        }

        foreach (var pl in scene.WallPolylines)
            EmitPolylineAsLines(w, "CAVE_WALL", pl.Points, pl.Closed, EmitDxfLine);
        foreach (var pl in scene.VectorPolylines)
            EmitPolylineAsLines(w, "CAVE_VECTOR", pl.Points, pl.Closed, EmitDxfLine);
        foreach (var (x1, y1, x2, y2) in scene.TraverseSegments)
            EmitDxfLine("CAVE_TRAVERSE", x1, y1, x2, y2);

        w.WriteLine("0");
        w.WriteLine("ENDSEC");
        w.WriteLine("0");
        w.WriteLine("EOF");
    }

    private static void EmitPolylineAsLines(
        TextWriter w,
        string layer,
        IReadOnlyList<(float x, float y)> pts,
        bool closed,
        Action<string, double, double, double, double> line)
    {
        if (pts.Count < 2)
            return;
        for (var i = 0; i < pts.Count - 1; i++)
            line(layer, pts[i].x, pts[i].y, pts[i + 1].x, pts[i + 1].y);
        if (closed && pts.Count >= 3)
            line(layer, pts[^1].x, pts[^1].y, pts[0].x, pts[0].y);
    }
}
