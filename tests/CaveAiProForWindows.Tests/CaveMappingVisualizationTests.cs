using System.IO;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Visualization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveMappingVisualizationTests
{
    private static CaveProjectDocument SampleProject() =>
        new()
        {
            Name = "TestCave",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "A",
                    ToStation = "B",
                    Distance = 10,
                    Azimuth = 0,
                    Clino = 0,
                    L = 2f,
                    R = 1.5f,
                    U = 1.2f,
                    D = 0.8f,
                },
                new ShotRecord
                {
                    FromStation = "B",
                    ToStation = "C",
                    Distance = 8,
                    Azimuth = 90,
                    Clino = -15,
                    L = 1f,
                    R = 2f,
                    U = 2f,
                    D = 1f,
                },
            ],
        };

    [TestMethod]
    public void SceneFactory_builds_2d_plan_and_profile_scenes()
    {
        var project = SampleProject();

        var plan = CaveMappingSceneFactory.Build(project, CaveMappingViewKind.PlanDrafting2D);
        var profile = CaveMappingSceneFactory.Build(project, CaveMappingViewKind.ExtendedProfile2D);

        Assert.IsFalse(plan.Is3D);
        Assert.IsNotNull(plan.Scene2D);
        Assert.IsTrue(plan.HasDrawableGeometry);

        Assert.IsFalse(profile.Is3D);
        Assert.IsNotNull(profile.Scene2D);
        Assert.IsTrue(profile.Scene2D!.SpanX > 0);
    }

    [TestMethod]
    public void TopographySurfaceGridBuilder_idw_returns_station_z_at_anchor()
    {
        var coords = new Dictionary<string, SurveyStationGeometry.StationPlanCoords>
        {
            ["A"] = new("A", 0f, 0f, 100f),
            ["B"] = new("B", 10f, 0f, 110f),
            ["C"] = new("C", 5f, 8f, 105f),
        };

        Assert.AreEqual(100f, TopographySurfaceGridBuilder.SampleIdwElevation(coords, 0, 0), 1e-4);
        Assert.AreEqual(110f, TopographySurfaceGridBuilder.SampleIdwElevation(coords, 10, 0), 1e-4);
        var mid = TopographySurfaceGridBuilder.SampleIdwElevation(coords, 5, 0);
        Assert.IsTrue(mid > 100f && mid < 110f);
    }

    [TestMethod]
    public void TopographySurfaceGridBuilder_builds_spec_from_coordinates()
    {
        var spec = TopographySurfaceGridBuilder.TryBuildSpec(SampleProject());
        Assert.IsNotNull(spec);
        Assert.IsTrue(spec!.MaxX > spec.MinX);
        Assert.IsTrue(spec.MaxY > spec.MinY);
    }

    [TestMethod]
    public void SymbolCatalog_returns_frozen_geometry_for_each_kind()
    {
        foreach (CaveMappingSymbolCatalog.SymbolKind kind in Enum.GetValues(typeof(CaveMappingSymbolCatalog.SymbolKind)))
        {
            var g = CaveMappingSymbolCatalog.GetGeometry(kind);
            Assert.IsTrue(g.IsFrozen);
            Assert.IsTrue(g.Bounds.Width > 0 && g.Bounds.Height > 0);
        }
    }

    [TestMethod]
    public void CatmullRomSampler_increases_point_count_for_bent_chain()
    {
        var chain = new List<(float x, float y)>
        {
            (0, 0), (5, 0), (5, 5), (10, 5),
        };
        var smooth = SurveyCatmullRomSampler.SampleOpenPlanChain(chain, targetSpacingM: 0.5f);
        Assert.IsTrue(smooth.Count > chain.Count);
    }

    [TestMethod]
    public void LrudRibbon_has_more_vertices_after_CatmullRom_geometry_pass()
    {
        var project = SampleProject();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var ribbons = SurveyLrudWallGeometry.BuildPlanLrudRibbonPolylines(project.Shots, coords);
        Assert.AreEqual(1, ribbons.Count);
        Assert.IsTrue(ribbons[0].Points.Count >= 12);
    }

    [TestMethod]
    public void ContinuousTubeMesh_builds_for_sample_traverse()
    {
        var project = SampleProject();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var mesh = CaveSurveyTubeMeshBuilder.BuildContinuousLrudTubeMesh(project.Shots, coords);
        Assert.IsNotNull(mesh);
        Assert.IsTrue(mesh!.Positions.Count > 24);
        Assert.IsTrue(mesh.TriangleIndices.Count > 36);
    }

    [TestMethod]
    public void SymbolPlacer_adds_station_and_junction_markers()
    {
        var project = SampleProject();
        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var auto = CaveMappingSymbolPlacer.BuildAutoPlanSymbols(project, coords, project.Shots);
        Assert.IsTrue(auto.Count >= 2);
        Assert.IsTrue(auto.Any(s => s.IconKey == CaveMappingSymbolCatalog.IconKey(CaveMappingSymbolCatalog.SymbolKind.StationMarker)));
    }

    [TestMethod]
    public void ExportMetadata_reads_project_name_and_date()
    {
        var project = SampleProject();
        project.Date = "2026-06-01";
        var meta = CaveMappingExportMetadata.FromProject(project);
        Assert.AreEqual("TestCave", meta.ProjectName);
        Assert.AreEqual("2026-06-01", meta.SurveyDate);
        StringAssert.Contains(meta.BuildSubtitleLine(), "2026-06-01");
    }

    [TestMethod]
    public void MapSymbolIconResolver_resolves_caveai_catalog_keys()
    {
        var sym = new SurveyStationGeometry.PlanMapSymbol(
            0, 0, 0, "A", 1f, 0f,
            CaveMappingSymbolCatalog.IconKey(CaveMappingSymbolCatalog.SymbolKind.JunctionMarker),
            CaveMappingSymbolCatalog.IconKey(CaveMappingSymbolCatalog.SymbolKind.JunctionMarker));
        var resolved = MapSymbolIconResolver.Resolve(sym, highContrast: false);
        Assert.AreEqual(MapSymbolIconResolver.RenderMode.VectorPath, resolved.Mode);
        Assert.IsNotNull(resolved.Geometry);
    }

    [TestMethod]
    public void WatermarkRenderer_embeds_logo_in_png_bytes()
    {
        SketchAssistTestsRunSta.Run(() =>
        {
            if (Application.Current == null)
                _ = new Application();

            using var bmp = new SKBitmap(128, 96);
            bmp.Erase(SKColors.White);
            using var encoded = bmp.Encode(SKEncodedImageFormat.Png, 100);
            var raw = encoded.ToArray();

            var watermarked = CaveMappingWatermarkRenderer.ApplyToPngBytes(raw);
            Assert.IsTrue(watermarked.Length > 64);
            Assert.AreEqual(0x89, watermarked[0]);
        });
    }
}
