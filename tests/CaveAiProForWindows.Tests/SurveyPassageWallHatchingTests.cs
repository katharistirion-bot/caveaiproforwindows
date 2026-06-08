using System.Windows;
using System.Windows.Media;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyPassageWallHatchingTests
{
    [TestMethod]
    public void Wall_hatching_spacing_scales_with_px_per_metre()
    {
        var lowZoom = SurveyPassageWallHatching.ComputeSpacingPx(pxPerMetre: 4, isPrintPreset: false);
        var highZoom = SurveyPassageWallHatching.ComputeSpacingPx(pxPerMetre: 24, isPrintPreset: false);
        Assert.IsTrue(highZoom > lowZoom);
        Assert.IsTrue(lowZoom >= 5.0);
        Assert.IsTrue(highZoom <= 16.0);
    }

    [TestMethod]
    public void Wall_hatching_print_spacing_stays_crisp_at_high_dpi()
    {
        var printSpacing = SurveyPassageWallHatching.ComputeSpacingPx(pxPerMetre: 80, isPrintPreset: true);
        Assert.IsTrue(printSpacing >= 4.0);
        Assert.IsTrue(printSpacing <= 12.0);
    }

    [TestMethod]
    public void Wall_hatching_lines_use_shared_global_phase_grid()
    {
        const double spacing = 8.0;
        var phaseOrigin = new Point(0, 0);
        var left = new Rect(0, 0, 40, 30);
        var right = new Rect(40, 0, 40, 30);

        var leftLines = SurveyPassageWallHatching.BuildHatchLineGeometry(left, spacing, phaseOrigin);
        var leftAgain = SurveyPassageWallHatching.BuildHatchLineGeometry(left, spacing, phaseOrigin);
        var rightLines = SurveyPassageWallHatching.BuildHatchLineGeometry(right, spacing, phaseOrigin);

        Assert.AreEqual(leftLines.Children.Count, leftAgain.Children.Count);

        static HashSet<int> QuantizedOffsets(GeometryGroup group, double spacingPx)
        {
            var angleRad = SurveyPassageWallHatching.HatchAngleDegrees * Math.PI / 180.0;
            var normX = -Math.Sin(angleRad);
            var normY = Math.Cos(angleRad);
            var set = new HashSet<int>();
            foreach (var child in group.Children)
            {
                if (child is not LineGeometry line)
                    continue;
                var midX = (line.StartPoint.X + line.EndPoint.X) * 0.5;
                var midY = (line.StartPoint.Y + line.EndPoint.Y) * 0.5;
                var off = midX * normX + midY * normY;
                set.Add((int)Math.Round(off / spacingPx * 1000.0));
            }

            return set;
        }

        var leftOffsets = QuantizedOffsets(leftLines, spacing);
        var rightOffsets = QuantizedOffsets(rightLines, spacing);
        var shared = leftOffsets.Intersect(rightOffsets).ToList();
        Assert.IsTrue(shared.Count >= 3, "Adjacent LRUD tiles should share hatch grid lines at the joint.");
    }
}
