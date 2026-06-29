using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.SketchAssist;

/// <summary>LRUD corridor polygons (grey on black) for cloud publish structure masks.</summary>
public static class StructureMaskLrudCorridorRenderer
{
    public static void DrawLrudCorridors(
        Canvas canvas,
        SketchAssistSession session,
        PlanCanvasSurveyLayout layout)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(session);

        var project = session.Project;
        var shots = project.Shots;
        if (shots == null || shots.Count == 0)
            return;

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var masks = SurveyLrudWallGeometry.BuildPlanLrudRibbonMaskPolygons(shots, coords);
        if (masks.Count == 0)
            masks = SurveyLrudWallGeometry.BuildPlanLrudCorridorMaskQuads(shots, coords);

        foreach (var mask in masks)
            DrawFilledSurveyPolygon(canvas, layout, mask.Outline.Points, mask.GreyLevel);
    }

    public static void DrawFilledSurveyPolygon(
        Canvas canvas,
        PlanCanvasSurveyLayout layout,
        IReadOnlyList<(float x, float y)> surveyPoints,
        byte greyLevel)
    {
        if (surveyPoints.Count < 3)
            return;

        var poly = new Polygon
        {
            Fill = GreyBrush(greyLevel),
            Stroke = GreyBrush(greyLevel),
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };

        foreach (var (x, y) in surveyPoints)
            poly.Points.Add(layout.WorldToCanvas(x, y));

        canvas.Children.Add(poly);
    }

    private static SolidColorBrush GreyBrush(byte grey)
    {
        var brush = new SolidColorBrush(Color.FromRgb(grey, grey, grey));
        brush.Freeze();
        return brush;
    }
}