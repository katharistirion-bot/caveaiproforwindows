using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class NamedCartographyDocumentsTests
{
    [TestMethod]
    public void ListAndOpen_namedMapsSwitchLiveSketches()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[0,0],[1,0]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0],[1,0]]],"vectorLines":[]},
                {"id":"b","name":"Flood overlay","sketches":[[[2,2],[3,3]]],"vectorLines":[{"type":"WATER","viewMode":0,"points":[{"first":1.0,"second":1.0}]}]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var list = NamedCartographyDocuments.List(project);
        Assert.AreEqual(2, list.Count);
        Assert.IsTrue(list[0].IsOpen);
        Assert.IsTrue(NamedCartographyDocuments.TryOpen(project!, "b"));
        Assert.AreEqual("b", project!.ActiveCartographyMapId);
        Assert.AreEqual(JsonValueKind.Array, project.Sketches.ValueKind);
        Assert.AreEqual(1, project.Sketches.GetArrayLength());
        Assert.AreEqual(JsonValueKind.Array, project.VectorLines?.ValueKind);
    }
}
