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
}
