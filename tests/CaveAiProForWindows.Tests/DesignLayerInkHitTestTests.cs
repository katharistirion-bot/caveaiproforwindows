using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class DesignLayerInkHitTestTests
{
    [TestMethod]
    public void IsEditableInk_RejectsSelectionFrameTag()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var rect = new System.Windows.Shapes.Rectangle { Tag = DesignLayerInkHitTest.SelectionFrameTag };
            Assert.IsFalse(DesignLayerInkHitTest.IsEditableInk(rect));
        });
    }

    [TestMethod]
    public void FindTopmostHit_SelectsPolylineNearSegment()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var canvas = new System.Windows.Controls.Canvas();
            var poly = new System.Windows.Shapes.Polyline
            {
                StrokeThickness = 2,
                Points = { new(10, 10), new(90, 90) },
            };
            canvas.Children.Add(poly);

            var hit = DesignLayerInkHitTest.FindTopmostHit(canvas, new System.Windows.Point(50, 50), 8);
            Assert.AreSame(poly, hit);
        });
    }
}
