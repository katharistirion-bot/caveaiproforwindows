using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class CaveViewport3DAnnotationsTests
{
    [TestMethod]
    public void Build_includes_splay_segments_and_leg_labels()
    {
        var project = new CaveProjectDocument
        {
            Name = "Demo Cave",
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "2",
                    Distance = 10f,
                    Azimuth = 90,
                    Clino = -5,
                    L = 1,
                    R = 1,
                    U = 0.5f,
                    D = 0.5f,
                },
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "-",
                    Azimuth = 0,
                    Clino = 0,
                    L = 2f,
                },
            ],
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var (lines, labels) = CaveViewport3DAnnotationsBuilder.Build(project, coords);

        Assert.IsTrue(lines.SplaySegments.Count >= 1);
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.Station));
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.Leg));
    }

    [TestMethod]
    public void Build_respects_show_labels_off()
    {
        var project = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 5f, Azimuth = 0, Clino = 0 },
            ],
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var (_, labels) = CaveViewport3DAnnotationsBuilder.Build(
            project,
            coords,
            Viewport3DAnnotationOptions.AllOff);

        Assert.AreEqual(0, labels.Count);
    }

    [TestMethod]
    public void Leg_labels_include_chainage_and_horizontal_length()
    {
        var project = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord
                {
                    FromStation = "1",
                    ToStation = "2",
                    Distance = 10f,
                    Azimuth = 90,
                    Clino = -10,
                    Depth = 2f,
                    L = 1,
                    R = 1,
                    U = 0.5f,
                    D = 0.5f,
                },
                new ShotRecord
                {
                    FromStation = "2",
                    ToStation = "3",
                    Distance = 8f,
                    Azimuth = 45,
                    Clino = 5,
                },
            ],
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var (_, labels) = CaveViewport3DAnnotationsBuilder.Build(project, coords);
        var leg = labels.FirstOrDefault(l => l.Kind == Viewport3DLabelKind.Leg);
        Assert.IsNotNull(leg);
        var text = string.Join(" ", leg.Lines);
        StringAssert.Contains(text, "1");
        StringAssert.Contains(text, "2");
        StringAssert.Contains(text, "S ");
        StringAssert.Contains(text, "L1");
    }

    [TestMethod]
    public void Build_includes_android_map_symbols_and_field_catalog()
    {
        var project = new CaveProjectDocument
        {
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 5f, Azimuth = 0, Clino = 0 },
            ],
            MapSymbols = System.Text.Json.JsonDocument.Parse(
                """[{"x":2.5,"y":0,"symbol":"pit","viewMode":0}]""").RootElement,
            FieldCatalogEntries = System.Text.Json.JsonDocument.Parse(
                """[{"planMapX":2.5,"planMapY":0.5,"name":"Bat colony","kind":"BIOTA","stationName":"1"}]""").RootElement,
            StationEnvironmentSnapshots =
            [
                new StationEnvironmentSnapshot
                {
                    StationName = "1",
                    ManualAmbientTempCelsius = 12.5f,
                    Notes = "Drafty passage",
                },
            ],
            SurveyAiClassifications =
            [
                new SurveyAiClassificationTag
                {
                    EntityType = "station",
                    EntityRef = "1",
                    Label = "Flowstone",
                    Confidence = 0.82f,
                },
            ],
        };

        var coords = SurveyStationGeometry.CalculatePlanCoordinates(project);
        var (_, labels) = CaveViewport3DAnnotationsBuilder.Build(project, coords);

        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.Symbol));
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.FieldCatalog));
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.StationSnapshot));
        Assert.IsTrue(labels.Any(l => l.Kind == Viewport3DLabelKind.AiTag));
    }
}
