using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace CaveAiProForWindows.Services;

/// <summary>Speleological passage fill/stroke styling for LRUD ribbon walls on plan and section canvases.</summary>
public static class SurveyPassageWallStyle
{
    public sealed record PassageBrushes(Brush Fill, Brush OuterStroke, Brush? InnerRimStroke);

    public static PassageBrushes CreatePlanPassageBrushes(
        bool darkCanvas,
        bool highContrast,
        bool plan2Tone,
        bool splayXRay,
        bool richIntensity,
        bool printPreset = false)
    {
        if (printPreset && !highContrast)
        {
            var fill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x68 : 0xA8), 0xF0, 0xE8, 0xDC));
            var stroke = new SolidColorBrush(Color.FromArgb(0xEE, 0x3D, 0x28, 0x1C));
            fill.Freeze();
            stroke.Freeze();
            return new PassageBrushes(fill, stroke, null);
        }

        if (highContrast)
        {
            var fill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x38 : 0x58), 0xFF, 0xFF, 0xFF));
            var stroke = Brushes.White;
            return new PassageBrushes(fill, stroke, null);
        }

        if (plan2Tone)
        {
            return new PassageBrushes(
                new SolidColorBrush(Color.FromArgb(0x32, 0x68, 0x60, 0x58)),
                new SolidColorBrush(Color.FromArgb(220, 0x38, 0x32, 0x2C)),
                new SolidColorBrush(Color.FromArgb(80, 0xC8, 0xBC, 0xAC)));
        }

        if (darkCanvas)
        {
            var fill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x48 : 0x70), 0x58, 0x50, 0x48));
            var stroke = new SolidColorBrush(Color.FromArgb(0xDD, 0xC8, 0xB8, 0xA8));
            var rim = richIntensity
                ? new SolidColorBrush(Color.FromArgb(0x55, 0xF0, 0xE8, 0xDC))
                : null;
            return new PassageBrushes(fill, stroke, rim);
        }

        // Limestone passage: warm centre, rock-brown rim (classic cave-map ink).
        var center = Color.FromArgb((byte)(splayXRay ? 0x58 : 0x92), 0xE8, 0xDC, 0xC8);
        var edge = Color.FromArgb((byte)(splayXRay ? 0x42 : 0x78), 0xA8, 0x90, 0x78);
        var outer = Color.FromArgb(0xDD, 0x5C, 0x40, 0x33);
        var rimLight = richIntensity
            ? new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xF8, 0xEE))
            : null;

        return new PassageBrushes(
            CreateRelativeRadialFill(center, edge),
            new SolidColorBrush(outer),
            rimLight);
    }

    public static PassageBrushes CreateProfilePassageBrushes(
        bool sectionNight,
        bool highContrast,
        bool splayXRay,
        bool printPreset = false)
    {
        if (printPreset && !highContrast)
        {
            var fill = new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 0x58 : 0x98), 0xE8, 0xDC, 0xCC));
            var stroke = new SolidColorBrush(Color.FromArgb(0xEE, 0x3D, 0x28, 0x1C));
            fill.Freeze();
            stroke.Freeze();
            return new PassageBrushes(fill, stroke, null);
        }

        if (highContrast)
            return new PassageBrushes(
                new SolidColorBrush(Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF)),
                Brushes.White,
                null);

        if (sectionNight)
        {
            return new PassageBrushes(
                new SolidColorBrush(Color.FromArgb((byte)(splayXRay ? 28 : 40), 0x78, 0x70, 0x68)),
                new SolidColorBrush(Color.FromRgb(0xF0, 0xE8, 0xDC)),
                new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xF8, 0xEE)));
        }

        return new PassageBrushes(
            CreateRelativeRadialFill(
                Color.FromArgb((byte)(splayXRay ? 0x48 : 0x80), 0xD8, 0xCE, 0xBC),
                Color.FromArgb((byte)(splayXRay ? 0x38 : 0x68), 0x98, 0x82, 0x6C)),
            new SolidColorBrush(Color.FromArgb(0xCC, 0x5C, 0x40, 0x33)),
            new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xF8, 0xEE)));
    }

    public static Brush CreateRelativeRadialFill(Color center, Color edge)
    {
        var brush = new RadialGradientBrush(center, edge)
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.42, 0.38),
            RadiusX = 0.72,
            RadiusY = 0.72,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>When LRUD passage hull is drawn, centreline is de-emphasised (thin dashed guide).</summary>
    public static (Brush Stroke, double ThicknessScale, double Opacity, bool Dashed) ResolveTraverseStyleWhenPassageFilled(
        bool hasFilledLrudPassage,
        Brush standardStroke,
        bool richIntensity)
    {
        if (!hasFilledLrudPassage)
            return (standardStroke, 1.0, 1.0, false);

        var guide = new SolidColorBrush(Color.FromArgb(0xAA, 0x6B, 0x5A, 0x4A));
        guide.Freeze();
        return (guide, richIntensity ? 0.42 : 0.52, 0.72, true);
    }

    public static bool SceneHasFilledLrudPassage(PlanScene scene, bool splayXRay, bool plan2Tone) =>
        !splayXRay && !plan2Tone &&
        scene.WallPolylines.Any(p => p is { Closed: true, Type: "lrudPlanRibbon" or "lrudPlan" or "lrudProfile" }
                                  && p.Points.Count >= 3);
}
