using System.Printing;
using System.Windows;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public class PrintLayoutServiceTests
{
    [TestMethod]
    public void GetPageSize_A4Portrait_MatchesIsoDimensions()
    {
        var size = PrintLayoutService.GetPageSize(PrintPaperSize.A4, PageOrientation.Portrait);
        Assert.AreEqual(210 * 96.0 / 25.4, size.Width, 0.5);
        Assert.AreEqual(297 * 96.0 / 25.4, size.Height, 0.5);
    }

    [TestMethod]
    public void GetPageSize_A3Landscape_SwapsWidthAndHeight()
    {
        var portrait = PrintLayoutService.GetPageSize(PrintPaperSize.A3, PageOrientation.Portrait);
        var landscape = PrintLayoutService.GetPageSize(PrintPaperSize.A3, PageOrientation.Landscape);
        Assert.AreEqual(portrait.Width, landscape.Height, 0.5);
        Assert.AreEqual(portrait.Height, landscape.Width, 0.5);
    }

    [TestMethod]
    public void GetPageSize_A2_IsLargerThanA4()
    {
        var a2 = PrintLayoutService.GetPageSize(PrintPaperSize.A2, PageOrientation.Landscape);
        var a4 = PrintLayoutService.GetPageSize(PrintPaperSize.A4, PageOrientation.Landscape);
        Assert.IsTrue(a2.Width > a4.Width);
        Assert.IsTrue(a2.Height > a4.Height);
    }

    [TestMethod]
    public void ToPageMediaSizeName_MapsIsoSizes()
    {
        Assert.AreEqual(PageMediaSizeName.ISOA4, PrintLayoutService.ToPageMediaSizeName(PrintPaperSize.A4));
        Assert.AreEqual(PageMediaSizeName.ISOA3, PrintLayoutService.ToPageMediaSizeName(PrintPaperSize.A3));
        Assert.AreEqual(PageMediaSizeName.ISOA2, PrintLayoutService.ToPageMediaSizeName(PrintPaperSize.A2));
    }
}
