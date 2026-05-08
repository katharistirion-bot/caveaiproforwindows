using System;
using CaveAiProForWindows.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Editable QC row for a traverse station; X/Y/Z edits write <see cref="CaveProjectDocument.PlanStationPositionOverrides"/>.</summary>
public partial class StationQcRowViewModel : ObservableObject
{
    private readonly CaveProjectDocument _project;
    private readonly Action _onCoordinateOverrideEdited;
    private bool _deferPush = true;

    public string Name { get; }
    public int TraverseLegsFrom { get; }
    public int TraverseLegsTo { get; }

    [ObservableProperty] private float _x;
    [ObservableProperty] private float _y;
    [ObservableProperty] private float _z;

    public StationQcRowViewModel(
        CaveProjectDocument project,
        string name,
        float x,
        float y,
        float z,
        int traverseLegsFrom,
        int traverseLegsTo,
        Action onCoordinateOverrideEdited)
    {
        _project = project;
        Name = name;
        X = x;
        Y = y;
        Z = z;
        TraverseLegsFrom = traverseLegsFrom;
        TraverseLegsTo = traverseLegsTo;
        _onCoordinateOverrideEdited = onCoordinateOverrideEdited;
        _deferPush = false;
    }

    partial void OnXChanged(float value) => PushOverrideIfReady();

    partial void OnYChanged(float value) => PushOverrideIfReady();

    partial void OnZChanged(float value) => PushOverrideIfReady();

    private void PushOverrideIfReady()
    {
        if (_deferPush)
            return;
        _project.PlanStationPositionOverrides[Name] = new PlanStationPositionOverride(X, Y, Z);
        _onCoordinateOverrideEdited();
    }
}
