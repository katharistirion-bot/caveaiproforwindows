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
        Assert.AreEqual(1, project.VectorLines?.GetArrayLength());
        Assert.AreEqual(JsonValueKind.Array, project.SectionSketches.ValueKind);
        Assert.AreEqual(0, project.SectionSketches.GetArrayLength());
    }

    [TestMethod]
    public void TryOpen_capturesLiveInkIntoPreviousMap()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[9,9],[8,8]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0],[1,0]]]},
                {"id":"b","name":"Flood overlay","sketches":[[[2,2],[3,3]]]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        Assert.IsTrue(NamedCartographyDocuments.TryOpen(project!, "b"));
        Assert.AreEqual("b", project!.ActiveCartographyMapId);
        Assert.AreEqual(1, project.Sketches.GetArrayLength());
        using var maps = JsonDocument.Parse(project.NamedCartographyMaps.GetRawText());
        var plan = maps.RootElement.EnumerateArray().First(m => m.GetProperty("id").GetString() == "a");
        Assert.AreEqual(1, plan.GetProperty("sketches").GetArrayLength());
        Assert.AreEqual(9, plan.GetProperty("sketches")[0][0][0].GetInt32());
    }

    [TestMethod]
    public void SyncLiveIntoActive_writesLiveSketchesBack()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[4,4],[5,5]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0]]]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsTrue(NamedCartographyDocuments.SyncLiveIntoActive(project!));
        using var maps = JsonDocument.Parse(project!.NamedCartographyMaps.GetRawText());
        var plan = maps.RootElement[0];
        Assert.AreEqual(1, plan.GetProperty("sketches").GetArrayLength());
        Assert.AreEqual(4, plan.GetProperty("sketches")[0][0][0].GetInt32());
        Assert.IsTrue(plan.TryGetProperty("updatedAtEpochMs", out var updated));
        Assert.IsTrue(updated.GetInt64() > 0);
    }

    [TestMethod]
    public void Prepare_coalescesUndefinedNamedCartographyMapsForSerialize()
    {
        var project = new CaveProjectDocument { Name = "Legacy" };
        CaveAiProForWindows.Services.Persistence.CaveProjectJsonWriteNormalizer.Prepare(project);
        var json = JsonSerializer.Serialize(project);
        StringAssert.Contains(json, "namedCartographyMaps");
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.GetProperty("namedCartographyMaps").ValueKind);
    }

    [TestMethod]
    public void TryCreateEmpty_capturesOpenMapThenClearsLive()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[9,9],[8,8]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0],[1,0]]]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var created = NamedCartographyDocuments.TryCreateEmpty(project!, "Flood overlay");
        Assert.AreEqual("Flood overlay", created.Name);
        Assert.AreEqual(created.Id, project!.ActiveCartographyMapId);
        Assert.AreEqual(0, project.Sketches.GetArrayLength());
        Assert.AreEqual(2, NamedCartographyDocuments.List(project).Count);
        using var maps = JsonDocument.Parse(project.NamedCartographyMaps.GetRawText());
        var plan = maps.RootElement.EnumerateArray().First(m => m.GetProperty("id").GetString() == "a");
        Assert.AreEqual(9, plan.GetProperty("sketches")[0][0][0].GetInt32());
        var empty = maps.RootElement.EnumerateArray().First(m => m.GetProperty("id").GetString() == created.Id);
        Assert.AreEqual(0, empty.GetProperty("sketches").GetArrayLength());
    }

    [TestMethod]
    public void TrySaveAs_copiesLiveIntoNewOpenMap()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[4,4],[5,5]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0]]]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var created = NamedCartographyDocuments.TrySaveAs(project!, "Plan");
        Assert.AreEqual("Plan 2", created.Name);
        Assert.AreEqual(created.Id, project!.ActiveCartographyMapId);
        Assert.AreEqual(1, project.Sketches.GetArrayLength());
        Assert.AreEqual(4, project.Sketches[0][0][0].GetInt32());
        using var maps = JsonDocument.Parse(project.NamedCartographyMaps.GetRawText());
        var copy = maps.RootElement.EnumerateArray().First(m => m.GetProperty("id").GetString() == created.Id);
        Assert.AreEqual(4, copy.GetProperty("sketches")[0][0][0].GetInt32());
    }

    [TestMethod]
    public void TryRename_uniqueNameAndUpdatedAt()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","updatedAtEpochMs":1},
                {"id":"b","name":"Flood overlay","updatedAtEpochMs":2}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var renamed = NamedCartographyDocuments.TryRename(project!, "a", "Flood overlay");
        Assert.AreEqual("Flood overlay 2", renamed);
        using var maps = JsonDocument.Parse(project!.NamedCartographyMaps.GetRawText());
        var plan = maps.RootElement.EnumerateArray().First(m => m.GetProperty("id").GetString() == "a");
        Assert.AreEqual("Flood overlay 2", plan.GetProperty("name").GetString());
        Assert.IsTrue(plan.GetProperty("updatedAtEpochMs").GetInt64() > 2);
    }

    [TestMethod]
    public void TryDelete_openMapOpensNewestRemaining()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[0,0]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0]]],"updatedAtEpochMs":1},
                {"id":"b","name":"Flood overlay","sketches":[[[2,2],[3,3]]],"updatedAtEpochMs":9}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var deleted = NamedCartographyDocuments.TryDelete(project!, "a");
        Assert.IsNotNull(deleted);
        Assert.AreEqual("b", deleted!.Value.OpenedId);
        Assert.AreEqual("b", project!.ActiveCartographyMapId);
        Assert.AreEqual(2, project.Sketches[0][0][0].GetInt32());
        Assert.AreEqual(1, NamedCartographyDocuments.List(project).Count);
    }

    [TestMethod]
    public void TryDelete_lastMapClearsLive()
    {
        var json = """
            {
              "name": "Xtenia",
              "activeCartographyMapId": "a",
              "sketches": [[[0,0]]],
              "namedCartographyMaps": [
                {"id":"a","name":"Plan","sketches":[[[0,0]]]}
              ]
            }
            """;
        var project = JsonSerializer.Deserialize<CaveProjectDocument>(json);
        Assert.IsNotNull(project);
        var deleted = NamedCartographyDocuments.TryDelete(project!, "a");
        Assert.IsNotNull(deleted);
        Assert.IsNull(deleted!.Value.OpenedId);
        Assert.AreEqual("", project!.ActiveCartographyMapId);
        Assert.AreEqual(0, project.Sketches.GetArrayLength());
        Assert.AreEqual(0, NamedCartographyDocuments.List(project).Count);
    }
}
