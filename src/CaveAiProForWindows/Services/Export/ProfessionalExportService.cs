using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services.Export;

public enum ProfessionalExportFormat
{
    Svg,
    Dxf,
    Kml,
    Png,
    Pdf,
}

public sealed class ProfessionalExportRequest
{
    public required CaveProjectDocument Project { get; init; }

    public required string OutputPath { get; init; }

    public ProfessionalExportFormat Format { get; init; } = ProfessionalExportFormat.Svg;

    public int VectorViewMode { get; init; } = SurveyStationGeometry.AndroidViewModePlan;

    public SurveyVisualizationMode VisualizationMode { get; init; } = SurveyVisualizationMode.Standard;

    public bool ApplyWatermark { get; init; } = true;

    public bool HighContrast { get; init; }

    public PlanCanvasDrawOptions? DrawOptions { get; init; }

    public string? ZipPath { get; init; }
}

/// <summary>
/// Unified local export hub for CAD/GIS/vector formats with optional watermark on visual outputs.
/// </summary>
public static class ProfessionalExportService
{
    public static void Export(ProfessionalExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dir = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        switch (request.Format)
        {
            case ProfessionalExportFormat.Dxf:
                ExportDxf(request);
                break;
            case ProfessionalExportFormat.Kml:
                ExportKml(request);
                break;
            case ProfessionalExportFormat.Svg:
                ExportSvg(request);
                break;
            case ProfessionalExportFormat.Png:
            case ProfessionalExportFormat.Pdf:
                ExportViaMappingService(request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Format, null);
        }
    }

    private static void ExportDxf(ProfessionalExportRequest request)
    {
        using var fs = File.Create(request.OutputPath);
        using var w = new StreamWriter(fs, new UTF8Encoding(false));
        SurveyDxfExporter.WritePlanDxf(request.Project, w, request.VectorViewMode);
    }

    private static void ExportKml(ProfessionalExportRequest request)
    {
        using var fs = File.Create(request.OutputPath);
        using var w = new StreamWriter(fs, new UTF8Encoding(false));
        SurveyKmlExporter.WritePlanKml(request.Project, w, request.VectorViewMode);
    }

    private static void ExportSvg(ProfessionalExportRequest request)
    {
        var kind = request.VectorViewMode == SurveyStationGeometry.AndroidViewModeSection
            ? CaveMappingViewKind.ExtendedProfile2D
            : request.VisualizationMode == SurveyVisualizationMode.LongProfile
                ? CaveMappingViewKind.LongProfile2D
                : CaveMappingViewKind.PlanDrafting2D;

        CaveMappingExportService.Export(new CaveMappingExportRequest
        {
            ViewKind = kind,
            Project = request.Project,
            OutputPath = request.OutputPath,
            Format = CaveMappingExportFormat.Svg,
            ApplyWatermark = request.ApplyWatermark,
            DrawOptions = request.DrawOptions,
            HighContrast = request.HighContrast,
            ZipPath = request.ZipPath,
        });
    }

    private static void ExportViaMappingService(ProfessionalExportRequest request)
    {
        var kind = request.VectorViewMode == SurveyStationGeometry.AndroidViewModeSection
            ? CaveMappingViewKind.ExtendedProfile2D
            : CaveMappingViewKind.PlanDrafting2D;

        CaveMappingExportService.Export(new CaveMappingExportRequest
        {
            ViewKind = kind,
            Project = request.Project,
            OutputPath = request.OutputPath,
            Format = request.Format == ProfessionalExportFormat.Pdf
                ? CaveMappingExportFormat.Pdf
                : CaveMappingExportFormat.Png,
            ApplyWatermark = request.ApplyWatermark,
            DrawOptions = request.DrawOptions,
            HighContrast = request.HighContrast,
            ZipPath = request.ZipPath,
        });
    }
}
