using System.Windows.Controls;
using System.Windows.Shapes;
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

    [TestMethod]
    public void DesignLayerInkMover_translates_polyline_and_symbol()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var poly = new Polyline
            {
                StrokeThickness = 2,
                Points = { new(10, 20), new(30, 40) },
            };
            Assert.IsTrue(DesignLayerInkMover.TryTranslate(poly, 5, -3));
            Assert.AreEqual(15, poly.Points[0].X, 1e-6);
            Assert.AreEqual(17, poly.Points[0].Y, 1e-6);
            Assert.AreEqual(35, poly.Points[1].X, 1e-6);
            Assert.AreEqual(37, poly.Points[1].Y, 1e-6);

            var stamp = new Viewbox { Width = 24, Height = 24 };
            Canvas.SetLeft(stamp, 100);
            Canvas.SetTop(stamp, 80);
            Assert.IsTrue(DesignLayerInkMover.TryTranslate(stamp, -10, 4));
            Assert.AreEqual(90, Canvas.GetLeft(stamp), 1e-6);
            Assert.AreEqual(84, Canvas.GetTop(stamp), 1e-6);
        });
    }

    [TestMethod]
    public void SketchEditorHistory_undo_redo_restores_moved_stroke()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            var canvas = new Canvas();
            var poly = new Polyline
            {
                StrokeThickness = 2,
                Points = { new(0, 0), new(10, 0) },
            };
            canvas.Children.Add(poly);
            Assert.IsTrue(DesignLayerInkMover.TryTranslate(poly, 8, 2));

            var history = new SketchEditorHistory();
            history.PushMoved(poly, 8, 2);
            Assert.IsTrue(history.TryUndo(canvas));
            Assert.AreEqual(0, poly.Points[0].X, 1e-6);
            Assert.AreEqual(0, poly.Points[0].Y, 1e-6);
            Assert.IsTrue(history.TryRedo(canvas));
            Assert.AreEqual(8, poly.Points[0].X, 1e-6);
            Assert.AreEqual(2, poly.Points[0].Y, 1e-6);
            Assert.AreEqual(18, poly.Points[1].X, 1e-6);
        });
    }
}
