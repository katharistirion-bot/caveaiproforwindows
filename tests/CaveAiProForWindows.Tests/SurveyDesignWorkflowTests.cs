using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class SurveyDesignWorkflowTests
{
    [TestMethod]
    public void ShouldOfferDesignAfterMapping_when_traverse_exists_and_mapObjects_empty()
    {
        var project = new CaveProjectDocument
        {
            Name = "Test cave",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10 },
            ],
            MapObjects = JsonSerializer.SerializeToElement(Array.Empty<object>()),
        };

        Assert.IsTrue(SurveyDesignWorkflow.HasTraverseForDesign(project));
        Assert.IsFalse(SurveyDesignWorkflow.HasExistingDesignLayer(project));
        Assert.IsTrue(SurveyDesignWorkflow.ShouldOfferDesignAfterMapping(project));
    }

    [TestMethod]
    public void ShouldOfferDesignAfterMapping_false_when_mapObjects_has_three_or_more()
    {
        var project = new CaveProjectDocument
        {
            Name = "Designed cave",
            Shots =
            [
                new ShotRecord { FromStation = "1", ToStation = "2", Distance = 10 },
            ],
            MapObjects = JsonSerializer.SerializeToElement(new object[] { new { x = 1 }, new { x = 2 }, new { x = 3 } }),
        };

        Assert.IsFalse(SurveyDesignWorkflow.ShouldOfferDesignAfterMapping(project));
    }

    [TestMethod]
    public void ShouldOfferDesignAfterMapping_false_without_traverse_legs()
    {
        var project = new CaveProjectDocument
        {
            Name = "Empty",
            Shots = [],
            MapObjects = JsonSerializer.SerializeToElement(Array.Empty<object>()),
        };

        Assert.IsFalse(SurveyDesignWorkflow.ShouldOfferDesignAfterMapping(project));
    }
}