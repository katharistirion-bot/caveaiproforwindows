using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Detects when a loaded survey is ready for plan design in Sketch Editor.
/// </summary>
public static class SurveyDesignWorkflow
{
    public static bool HasTraverseForDesign(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Shots.Any(s => s.IsTraverseLeg);
    }

    public static int CountDesignLayerEntries(CaveProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.MapObjects.ValueKind == JsonValueKind.Array
            ? project.MapObjects.GetArrayLength()
            : 0;
    }

    public static bool HasExistingDesignLayer(CaveProjectDocument project) =>
        CountDesignLayerEntries(project) > 0;

    public static bool ShouldOfferDesignAfterMapping(CaveProjectDocument project) =>
        HasTraverseForDesign(project) && CountDesignLayerEntries(project) < 3;
}