using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyMapLabelLayoutTests
{
    [TestMethod]
    public void Resolve_thins_station_names_on_dense_traverse()
    {
        var project = new CaveProjectDocument { Name = "Dense" };
        for (var i = 1; i <= 40; i++)
        {
            project.Shots.Add(new ShotRecord
            {
                FromStation = i.ToString(),
                ToStation = (i + 1).ToString(),
                Distance = 1f,
                Azimuth = 88,
                Clino = 18,
            });
        }

        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan)!;
        var ann = SurveyMapAnnotationsBuilder.Build(project, scene, 0, legDetails: true, stationEnvironment: false);
        var opt = PlanCanvasDrawOptions.ForPlan(stationNames: true, overlay: true, showStationZDepth: true);
        Point ToScreen(float x, float y) => new(x * 12, y * 12);
        var layout = SurveyMapLabelLayout.Resolve(project, scene, ann, opt, ToScreen, pxPerMetre: 12);

        Assert.IsTrue(layout.StationNames.Count < scene.Stations.Count);
        Assert.IsTrue(layout.Legs.Count < ann.LegLabels.Count);
    }

    [TestMethod]
    public void Resolve_hides_leg_and_environment_below_8_px_per_metre()
    {
        var project = BuildLinearProject(12);
        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan)!;
        var ann = SurveyMapAnnotationsBuilder.Build(project, scene, 0, legDetails: true, stationEnvironment: true);
        var opt = PlanCanvasDrawOptions.ForPlan(
            stationNames: true,
            overlay: true,
            showStationZDepth: true,
            showLegSurveyDetails: true,
            showStationEnvironment: true);
        Point ToScreen(float x, float y) => new(x * 6, y * 6);

        var layout = SurveyMapLabelLayout.Resolve(project, scene, ann, opt, ToScreen, pxPerMetre: 6);

        Assert.IsTrue(layout.StationNames.Count > 0);
        Assert.AreEqual(0, layout.Legs.Count);
        Assert.AreEqual(0, layout.Environment.Count);
    }

    [TestMethod]
    public void Resolve_reduces_station_z_to_ends_below_4_px_per_metre()
    {
        var project = BuildLinearProject(20, legDistance: 8f);
        var scene = PlanSceneBuilder.TryBuild(project, SurveyStationGeometry.AndroidViewModePlan)!;
        var ann = SurveyMapAnnotationsBuilder.Build(project, scene, 0, legDetails: true, stationEnvironment: false);
        var opt = PlanCanvasDrawOptions.ForPlan(stationNames: false, overlay: true, showStationZDepth: true);
        Point ToScreenFull(float x, float y) => new(x * 15, y * 15);
        Point ToScreenLow(float x, float y) => new(x * 3, y * 3);

        var full = SurveyMapLabelLayout.Resolve(project, scene, ann, opt, ToScreenFull, pxPerMetre: 15);
        var reduced = SurveyMapLabelLayout.Resolve(project, scene, ann, opt, ToScreenLow, pxPerMetre: 3);

        Assert.IsTrue(full.StationZ.Count > 2);
        Assert.IsTrue(reduced.StationZ.Count <= 2);
        Assert.IsTrue(full.StationZ.Count > reduced.StationZ.Count);
    }

    private static CaveProjectDocument BuildLinearProject(int legCount, float legDistance = 2f)
    {
        var project = new CaveProjectDocument { Name = "Linear" };
        for (var i = 1; i <= legCount; i++)
        {
            project.Shots.Add(new ShotRecord
            {
                FromStation = i.ToString(),
                ToStation = (i + 1).ToString(),
                Distance = legDistance,
                Azimuth = 90,
                Clino = 0,
            });
        }

        return project;
    }
}
