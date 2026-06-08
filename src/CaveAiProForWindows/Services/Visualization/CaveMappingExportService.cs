using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.Visualization;

public enum CaveMappingExportFormat
{
    Png,
    Svg,
    Pdf,
}

public sealed class CaveMappingExportRequest
{
    public required CaveMappingViewKind ViewKind { get; init; }

    public required CaveProjectDocument Project { get; init; }

    public required string OutputPath { get; init; }

    public CaveMappingExportFormat Format { get; init; } = CaveMappingExportFormat.Png;

    public bool HighContrast { get; init; }

    public bool ApplyWatermark { get; init; } = true;

    public CaveMappingWatermarkOptions? Watermark { get; init; }

    public PlanCanvasDrawOptions? DrawOptions { get; init; }

    public CaveMappingExportMetadata? ExportMetadata { get; init; }

    public string? ZipPath { get; init; }

    public IReadOnlyList<PlanRasterUnderlay> Underlays { get; init; } = Array.Empty<PlanRasterUnderlay>();
}

/// <summary>
/// Unified export pipeline for plan, extended profile, long profile, and 3D topography views.
/// Delegates geometry to existing exporters; applies <see cref="CaveMappingWatermarkRenderer"/> on output.
/// </summary>
public static class CaveMappingExportService
{
    public static void Export(CaveMappingExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dir = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        switch (request.Format)
        {
            case CaveMappingExportFormat.Png:
                ExportPng(request);
                break;
            case CaveMappingExportFormat.Svg:
                ExportSvg(request);
                break;
            case CaveMappingExportFormat.Pdf:
                ExportPdf(request);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Format, null);
        }
    }

    private static void ExportPng(CaveMappingExportRequest request)
    {
        byte[]? raw = request.ViewKind switch
        {
            CaveMappingViewKind.Topography3D => PlanMapRasterExporter.TryCapture3DPng(request.Project),
            _ => Capture2DPng(request),
        };

        if (raw == null || raw.Length == 0)
            throw new InvalidOperationException("Nothing to export for this view and project.");

        if (request.ApplyWatermark)
            raw = CaveMappingWatermarkRenderer.ApplyToPngBytes(raw, request.Watermark);

        File.WriteAllBytes(request.OutputPath, raw);
    }

    private static void ExportPdf(CaveMappingExportRequest request)
    {
        var pngPath = Path.ChangeExtension(request.OutputPath, ".png");
        ExportPng(new CaveMappingExportRequest
        {
            ViewKind = request.ViewKind,
            Project = request.Project,
            OutputPath = pngPath,
            Format = CaveMappingExportFormat.Png,
            HighContrast = request.HighContrast,
            ApplyWatermark = request.ApplyWatermark,
            Watermark = request.Watermark,
            DrawOptions = request.DrawOptions
                          ?? CaveMappingExportCartography.BuildProfessionalDrawOptions(
                              request.Project,
                              request.ViewKind,
                              request.HighContrast),
            ExportMetadata = request.ExportMetadata,
            ZipPath = request.ZipPath,
            Underlays = request.Underlays,
        });
        var pngBytes = File.ReadAllBytes(pngPath);
        PlanPdfExport.SavePngBytesAsPdf(pngBytes, request.OutputPath);
        try
        {
            File.Delete(pngPath);
        }
        catch
        {
            /* temp png optional */
        }
    }

    private static void ExportSvg(CaveMappingExportRequest request)
    {
        if (request.ViewKind == CaveMappingViewKind.Topography3D)
            throw new NotSupportedException("SVG export is available for 2D drafting views only. Use PNG or PDF for 3D.");

        var (vectorMode, viz) = Resolve2DModes(request.ViewKind);
        using var fs = File.Create(request.OutputPath);
        using var buffer = new MemoryStream();
        var drawOpt = request.DrawOptions
                      ?? CaveMappingExportCartography.BuildProfessionalDrawOptions(
                          request.Project,
                          request.ViewKind,
                          request.HighContrast);
        if (request.ExportMetadata != null && drawOpt.ExportMetadata == null)
            drawOpt = drawOpt with { ExportMetadata = request.ExportMetadata };

        SurveySvgExporter.WritePlanSvg(
            request.Project,
            buffer,
            vectorMode,
            viz,
            drawOpt);

        if (!request.ApplyWatermark)
        {
            buffer.Position = 0;
            buffer.CopyTo(fs);
            return;
        }

        var svgText = Encoding.UTF8.GetString(buffer.ToArray());
        var descriptor = CaveMappingSceneFactory.Build(request.Project, request.ViewKind);
        var scene = descriptor.Scene2D
            ?? throw new InvalidOperationException("No drawable geometry for SVG.");
        var pad = 4f;
        var vbW = scene.SpanX + 2 * pad;
        var vbH = scene.SpanY + 2 * pad;

        var insertAt = svgText.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (insertAt < 0)
            throw new InvalidOperationException("SVG export malformed.");

        using var wm = new StringWriter();
        CaveMappingWatermarkRenderer.AppendSvgWatermark(wm, vbW, vbH, request.Watermark);
        var merged = svgText.Insert(insertAt, wm.ToString());
        File.WriteAllText(request.OutputPath, merged, new UTF8Encoding(false));
    }

    private static byte[]? Capture2DPng(CaveMappingExportRequest request)
    {
        var (vectorMode, viz) = Resolve2DModes(request.ViewKind);
        var opt = request.DrawOptions
                  ?? CaveMappingExportCartography.BuildProfessionalDrawOptions(
                      request.Project,
                      request.ViewKind,
                      request.HighContrast);
        if (request.ExportMetadata != null && opt.ExportMetadata == null)
            opt = opt with { ExportMetadata = request.ExportMetadata };

        var scene = PlanSceneBuilder.TryBuild(request.Project, Resolve2DModes(request.ViewKind).vectorMode, Resolve2DModes(request.ViewKind).viz);
        if (scene != null && opt.ExportMetadata != null)
        {
            var (pxW, _) = PlanMapRasterExporter.ComputeExportPixelSize(scene, MapExportQuality.Print);
            var pxPerMetre = pxW / Math.Max(scene.SpanX, 1e-6f);
            opt = opt with
            {
                ExportMetadata = opt.ExportMetadata with
                {
                    ScaleLabel = CartographicScaleCalculator.FormatScaleLabel(pxPerMetre),
                },
            };
        }

        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            request.Project,
            viz,
            request.HighContrast,
            opt,
            request.Underlays,
            request.ZipPath,
            quality: MapExportQuality.Print);
    }

    private static (int vectorMode, SurveyVisualizationMode viz) Resolve2DModes(CaveMappingViewKind kind) =>
        kind switch
        {
            CaveMappingViewKind.ExtendedProfile2D => (
                SurveyStationGeometry.AndroidViewModeSection,
                SurveyVisualizationMode.Standard),
            CaveMappingViewKind.LongProfile2D => (
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.LongProfile),
            _ => (
                SurveyStationGeometry.AndroidViewModePlan,
                SurveyVisualizationMode.Standard),
        };

    private static PlanCanvasDrawOptions DefaultDrawOptions(int vectorMode, SurveyVisualizationMode viz) =>
        new(
            ShowStationNames: true,
            ShowCartographyOverlay: true,
            CanvasKind: vectorMode == SurveyStationGeometry.AndroidViewModeSection
                ? SurveyCanvasKind.Section
                : SurveyCanvasKind.Plan,
            VisualizationMode: viz,
            ShowStationZDepth: vectorMode == SurveyStationGeometry.AndroidViewModePlan,
            CartographicIntensity: CartographicIntensity.Rich,
            ShowLegSurveyDetails: true,
            ShowStationEnvironment: true,
            ShowDepthSpanAnnotations: true,
            ShowBracketMarkers: true,
            ShowSymbolLegend: true);
}
