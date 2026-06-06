using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SketchAssist;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Host callbacks from <see cref="Views.SketchEditorView"/> for procedural sketch generation.</summary>
public sealed class ProceduralSketchEditorHost
{
    public required Func<CaveProjectDocument?> GetProject { get; init; }

    public required Func<bool> IsSurveyLayoutReady { get; init; }

    public required Func<PlanCanvasSurveyLayout> GetSurveyLayout { get; init; }

    public required Func<System.Windows.Controls.Canvas?> GetDesignLayer { get; init; }

    public Action? OnDesignLayerChanged { get; init; }
}

/// <summary>Sketch Assist Phase 2 — auto walls/symbols from survey data.</summary>
public partial class ProceduralSketchViewModel : ObservableObject
{
    private readonly ProceduralSketchEditorHost _host;

    public ProceduralSketchViewModel(ProceduralSketchEditorHost host) => _host = host;

    [ObservableProperty] private bool _includeWallOutlines = true;

    [ObservableProperty] private bool _includeMapSymbols = true;

    [ObservableProperty] private bool _includeFieldCatalogPins = true;

    [ObservableProperty] private bool _hasProceduralPreview;

    [ObservableProperty]
    private string _statusMessage =
        "Generate LRUD wall outlines and survey symbols onto the design layer (dashed preview).";

    partial void OnHasProceduralPreviewChanged(bool value)
    {
        ClearProceduralCommand.NotifyCanExecuteChanged();
        AcceptProceduralCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void GenerateProcedural()
    {
        var project = _host.GetProject();
        var canvas = _host.GetDesignLayer();
        if (project == null || canvas == null || !_host.IsSurveyLayoutReady())
        {
            StatusMessage = "Load a project with plan survey data first.";
            return;
        }

        var options = new ProceduralSketchOptions
        {
            IncludeWallOutlines = IncludeWallOutlines,
            IncludeMapSymbols = IncludeMapSymbols,
            IncludeFieldCatalogPins = IncludeFieldCatalogPins,
        };

        if (!ProceduralSketchGenerator.TryGenerate(project, options, out var result))
        {
            StatusMessage = "No procedural geometry could be derived from this survey.";
            HasProceduralPreview = false;
            return;
        }

        var layout = _host.GetSurveyLayout();
        var added = DesignLayerProceduralApplicator.Apply(canvas, layout, result);
        HasProceduralPreview = added > 0;
        StatusMessage = added > 0
            ? $"Placed {result.Strokes.Count} wall stroke(s) and {result.SymbolStamps.Count} symbol(s) — dashed = procedural preview."
            : "Nothing was added to the design layer.";
        _host.OnDesignLayerChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasProceduralPreview))]
    private void ClearProcedural()
    {
        var canvas = _host.GetDesignLayer();
        if (canvas == null)
            return;
        var removed = DesignLayerProceduralApplicator.RemoveProcedural(canvas);
        HasProceduralPreview = false;
        StatusMessage = removed > 0
            ? $"Removed {removed} procedural object(s)."
            : "No procedural preview on the design layer.";
        _host.OnDesignLayerChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(HasProceduralPreview))]
    private void AcceptProcedural()
    {
        var canvas = _host.GetDesignLayer();
        if (canvas == null)
            return;

        var count = 0;
        foreach (var child in canvas.Children.OfType<System.Windows.FrameworkElement>())
        {
            if (child.Tag is not DesignLayerInkMetadata meta)
                continue;
            if (!string.Equals(meta.Source, SketchStrokeStyleDefaults.ProceduralSource, StringComparison.OrdinalIgnoreCase))
                continue;

            child.Tag = new DesignLayerInkMetadata
            {
                Source = SketchStrokeStyleDefaults.DesignLayerSource,
                StrokeWidthPx = meta.StrokeWidthPx,
                StrokeColorArgb = SketchStrokeStyleDefaults.DefaultStrokeColorArgb,
                BrushProfile = SketchStrokeStyleDefaults.DefaultBrushProfile,
                LayerName = "Accepted procedural",
                LayerIndex = SketchStrokeStyleDefaults.UserLayerIndex,
                LayerZOrder = SketchStrokeStyleDefaults.UserLayerZOrder,
            };

            if (child is System.Windows.Shapes.Polyline poly)
            {
                poly.StrokeDashArray = null;
                poly.Opacity = 1;
                poly.Stroke = DesignLayerInkMetadata.ArgbToBrush(SketchStrokeStyleDefaults.DefaultStrokeColorArgb)
                    ?? System.Windows.Media.Brushes.Black;
            }
            else if (child is System.Windows.Controls.Viewbox vb)
                vb.Opacity = 1;

            count++;
        }

        HasProceduralPreview = false;
        StatusMessage = count > 0
            ? $"Accepted {count} procedural object(s) as editable ink."
            : "Nothing to accept.";
        _host.OnDesignLayerChanged?.Invoke();
    }

    private bool CanGenerate() => _host.GetProject() != null && _host.IsSurveyLayoutReady();

    public void NotifyProjectChanged()
    {
        HasProceduralPreview = false;
        GenerateProceduralCommand.NotifyCanExecuteChanged();
    }
}
