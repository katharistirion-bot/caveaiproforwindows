using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Visualization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CartographyPhaseCTests
{
    [TestMethod]
    public void Scale_calculator_formats_denominator_from_px_per_metre()
    {
        var label = CartographicScaleCalculator.FormatScaleLabel(10);
        Assert.IsTrue(label.StartsWith("1:", StringComparison.Ordinal));
        var n = int.Parse(label[2..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(n >= 300 && n <= 5000);
    }

    [TestMethod]
    public void Scale_calculator_from_spans_matches_px_per_metre()
    {
        const double spanM = 100;
        const double px = 250;
        var a = CartographicScaleCalculator.FormatScaleLabel(px / spanM);
        var b = CartographicScaleCalculator.FormatScaleLabelFromSpans(spanM, px);
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Export_metadata_title_block_includes_scale_north_and_printed_date()
    {
        var project = new CaveProjectDocument
        {
            Name = "DemoCave",
            Date = "2024-05-01",
            ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["surveyTeam"] = System.Text.Json.JsonSerializer.SerializeToElement("Alpha Grotto Club"),
            },
        };

        var meta = CaveMappingExportMetadata.FromProject(project, pxPerMetre: 12.5);
        var title = meta.BuildTitleBlockLine();
        Assert.IsTrue(title.Contains("Scale 1:", StringComparison.Ordinal));
        Assert.IsTrue(title.Contains("North up", StringComparison.Ordinal));
        Assert.IsTrue(title.Contains("Printed", StringComparison.Ordinal));
        Assert.IsTrue(title.Contains("Alpha Grotto Club", StringComparison.Ordinal));

        var subtitle = meta.BuildSubtitleLine();
        Assert.IsTrue(subtitle.Contains("Exported", StringComparison.Ordinal));
        Assert.IsTrue(subtitle.Contains("Scale 1:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Wall_hatching_policy_follows_explicit_toggle()
    {
        var printRich = new PlanCanvasDrawOptions(
            CartographicIntensity: CartographicIntensity.Rich,
            RenderPreset: CartographicRenderPreset.Print);
        Assert.IsFalse(WallHatchingPolicy.ShouldEnable(printRich));

        var printWithHatch = printRich with { ShowWallHatching = true };
        Assert.IsTrue(WallHatchingPolicy.ShouldEnable(printWithHatch));

        var fieldExplicit = new PlanCanvasDrawOptions(ShowWallHatching: true);
        Assert.IsTrue(WallHatchingPolicy.ShouldEnable(fieldExplicit));

        var fieldBalanced = new PlanCanvasDrawOptions(
            CartographicIntensity: CartographicIntensity.Balanced,
            RenderPreset: CartographicRenderPreset.Field);
        Assert.IsFalse(WallHatchingPolicy.ShouldEnable(fieldBalanced));
    }

    [TestMethod]
    public void ForExportPrint_uses_print_preset_and_metadata()
    {
        var project = new CaveProjectDocument { Name = "PrintCave", Date = "2025-01-01" };
        var opt = PlanCanvasDrawOptionsFactory.ForExportPrint(SurveyCanvasKind.Plan, project: project);
        Assert.AreEqual(CartographicRenderPreset.Print, opt.RenderPreset);
        Assert.IsTrue(opt.ShowWallHatching);
        Assert.IsNotNull(opt.ExportMetadata);
        Assert.IsFalse(string.IsNullOrWhiteSpace(opt.ExportMetadata!.NorthLabel));
    }

    [TestMethod]
    public void Symbol_catalog_legend_size_scales_with_px_per_metre()
    {
        var small = CaveMappingSymbolCatalog.LegendSymbolSizeDip(4);
        var large = CaveMappingSymbolCatalog.LegendSymbolSizeDip(40);
        Assert.IsTrue(large > small);
        Assert.IsTrue(small >= 14);
        Assert.IsTrue(large <= 28);
    }

    [TestMethod]
    public void ForRasterExport_print_uses_print_preset_and_wall_hatching_toggle()
    {
        var project = new CaveProjectDocument { Name = "RasterCave" };
        var withHatch = PlanCanvasDrawOptionsFactory.ForRasterExport(
            SurveyCanvasKind.Section,
            MapExportQuality.Print,
            project: project,
            showWallHatching: true);
        Assert.AreEqual(CartographicRenderPreset.Print, withHatch.RenderPreset);
        Assert.IsTrue(withHatch.ShowWallHatching);
        Assert.IsTrue(WallHatchingPolicy.ShouldEnable(withHatch));
        Assert.IsNotNull(withHatch.ExportMetadata);

        var withoutHatch = PlanCanvasDrawOptionsFactory.ForRasterExport(
            SurveyCanvasKind.Section,
            MapExportQuality.Print,
            project: project,
            showWallHatching: false);
        Assert.IsFalse(withoutHatch.ShowWallHatching);
        Assert.IsFalse(WallHatchingPolicy.ShouldEnable(withoutHatch));
    }

    [TestMethod]
    public void ForRasterExport_standard_uses_field_preset()
    {
        var opt = PlanCanvasDrawOptionsFactory.ForRasterExport(
            SurveyCanvasKind.Section,
            MapExportQuality.Standard);
        Assert.AreEqual(CartographicRenderPreset.Field, opt.RenderPreset);
        Assert.IsTrue(opt.ShowCartographyOverlay);
    }

    [TestMethod]
    public void MapExportQuality_parser_round_trips()
    {
        Assert.AreEqual(MapExportQuality.Print, MapExportQualityParser.Parse("Print"));
        Assert.AreEqual("Standard", MapExportQualityParser.ToPersistedString(MapExportQuality.Standard));
    }
}
