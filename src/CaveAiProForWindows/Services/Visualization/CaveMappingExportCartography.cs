using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Visualization;

/// <summary>Professional cartographic chrome for CaveAI map exports (legend, metadata, overlays).</summary>
public static class CaveMappingExportCartography
{
    private static readonly Brush LegendStroke = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
    private static readonly Brush LegendFill = new SolidColorBrush(Color.FromArgb(0x66, 0x45, 0xA8, 0x9A));

    static CaveMappingExportCartography()
    {
        LegendStroke.Freeze();
        LegendFill.Freeze();
    }

    public static PlanCanvasDrawOptions BuildProfessionalDrawOptions(
        CaveProjectDocument project,
        CaveMappingViewKind viewKind,
        bool highContrast,
        bool showStationNames = true)
    {
        var (vectorMode, viz) = Resolve2DModes(viewKind);
        var metadata = CaveMappingExportMetadata.FromProject(
            project,
            canvasKind: vectorMode == SurveyStationGeometry.AndroidViewModeSection
                ? SurveyCanvasKind.Section
                : SurveyCanvasKind.Plan);
        return new PlanCanvasDrawOptions(
            ShowStationNames: showStationNames,
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
            ShowLoopClosureHighlights: true,
            ShowLrudQcHighlights: true,
            ShowCoordinateGrid: false,
            ShowSymbolLegend: true,
            ShowWallHatching: true,
            RenderPreset: CartographicRenderPreset.Print,
            ExportMetadata: metadata);
    }

    public static void AppendSymbolLegend(
        Canvas canvas,
        double canvasWidth,
        double canvasHeight,
        bool highContrast,
        double pxPerMetre,
        int zIndex,
        Action<Canvas, UIElement, int> addChildZ)
    {
        var fg = highContrast
            ? Brushes.White
            : SurveyCanvasTheme.IsDark
                ? new SolidColorBrush(Color.FromRgb(0xE6, 0xEA, 0xEF))
                : new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38));
        var chipBg = highContrast
            ? new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
            : SurveyCanvasTheme.IsDark
                ? new SolidColorBrush(Color.FromArgb(210, 28, 33, 40))
                : new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
        var chipBorder = highContrast
            ? new SolidColorBrush(Color.FromRgb(40, 40, 40))
            : SurveyCanvasTheme.IsDark
                ? new SolidColorBrush(Color.FromRgb(80, 86, 96))
                : new SolidColorBrush(Color.FromRgb(190, 194, 200));

        var symbolSize = CaveMappingSymbolCatalog.LegendSymbolSizeDip(pxPerMetre);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = "Symbol legend (UICC-style)",
            Foreground = fg,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var kind in CaveMappingSymbolCatalog.DefaultLegendKinds)
            panel.Children.Add(BuildLegendRow(kind, fg, highContrast, symbolSize));

        var chip = new Border
        {
            Background = chipBg,
            BorderBrush = chipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 12, 8),
            Child = panel,
            MaxWidth = 240,
            SnapsToDevicePixels = true,
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        chip.Arrange(new Rect(chip.DesiredSize));
        Canvas.SetLeft(chip, Math.Max(8, canvasWidth - chip.DesiredSize.Width - 14));
        Canvas.SetTop(chip, 14);
        addChildZ(canvas, chip, zIndex);
    }

    public static void AppendMetadataFooter(
        Canvas canvas,
        CaveMappingExportMetadata metadata,
        double canvasWidth,
        double canvasHeight,
        Brush fg,
        int zIndex,
        Action<Canvas, UIElement, int> addChildZ)
    {
        var line = metadata.BuildSubtitleLine();
        if (string.IsNullOrWhiteSpace(line))
            return;

        var tb = new TextBlock
        {
            Text = line,
            Foreground = fg,
            FontSize = 10.5,
            FontStyle = FontStyles.Italic,
            MaxWidth = Math.Max(200, canvasWidth - 24),
            TextWrapping = TextWrapping.Wrap,
        };
        Canvas.SetLeft(tb, 12);
        Canvas.SetTop(tb, canvasHeight - 28);
        addChildZ(canvas, tb, zIndex);
    }

    private static UIElement BuildLegendRow(
        CaveMappingSymbolCatalog.SymbolKind kind,
        Brush fg,
        bool highContrast,
        double symbolSizeDip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var path = CaveMappingSymbolCatalog.CreatePath(
            kind,
            symbolSizeDip,
            highContrast ? Brushes.White : LegendFill,
            highContrast ? Brushes.White : LegendStroke,
            1.0);
        path.Width = symbolSizeDip;
        path.Height = symbolSizeDip;
        path.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(path);
        row.Children.Add(new TextBlock
        {
            Text = CaveMappingSymbolCatalog.GetLabel(kind),
            Foreground = fg,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
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
}
