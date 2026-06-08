using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Visualization;

namespace CaveAiProForWindows.Services.PublicationSheet;

/// <summary>
/// Composes plan, elevation, optional 3D, legend, and metadata into one publication sheet PNG.
/// Additive export path — does not alter existing plan/section/3D tabs or booklet PDF flow.
/// </summary>
public static class PublicationSheetComposer
{
    public const int DefaultSheetWidthDip = 1587;
    public const int DefaultSheetHeightDip = 1122;

    private const double MarginDip = 36;
    private const double HeaderHeightDip = 92;
    private const double PanelGapDip = 10;
    private const double LegendWidthDip = 220;

    public static PublicationSheetResult TryCompose(CaveProjectDocument project, PublicationSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);

        var panels = CapturePanels(project, options);
        if (panels.Elevation == null && panels.Plan == null)
            return new PublicationSheetResult(null, "No survey geometry available for a publication sheet.");

        var logicalW = options.SheetWidth;
        var logicalH = options.SheetHeight;
        var dpiScale = options.Quality.DpiScale();
        var outputW = Math.Max(800, (int)Math.Round(logicalW * dpiScale));
        var outputH = Math.Max(600, (int)Math.Round(logicalH * dpiScale));

        var root = BuildSheetVisual(project, panels, options, logicalW, logicalH);
        root.Measure(new Size(logicalW, logicalH));
        root.Arrange(new Rect(0, 0, logicalW, logicalH));
        root.UpdateLayout();

        var png = RenderToPng(root, logicalW, logicalH, outputW, outputH);
        return png == null
            ? new PublicationSheetResult(null, "Publication sheet render failed.")
            : new PublicationSheetResult(png);
    }

    private sealed record CapturedPanels(
        byte[]? Elevation,
        byte[]? Plan,
        byte[]? Overview3D,
        string ElevationLabel);

    private static PlanCanvasDrawOptions PublicationPanelDrawOptions(
        SurveyCanvasKind canvasKind,
        SurveyVisualizationMode visualizationMode,
        CaveProjectDocument project) =>
        PlanCanvasDrawOptionsFactory.ForExportPrint(canvasKind, visualizationMode, project) with
        {
            ShowCartographyOverlay = false,
            ShowSymbolLegend = false,
            ExportMetadata = null,
        };

    private static byte[]? CapturePlanPanel(
        CaveProjectDocument project,
        SurveyVisualizationMode visualizationMode,
        SurveyCanvasKind canvasKind,
        int vectorViewMode,
        MapExportQuality quality)
    {
        return PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            visualizationMode,
            highContrast: false,
            PublicationPanelDrawOptions(canvasKind, visualizationMode, project),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            vectorViewMode: vectorViewMode,
            quality: quality,
            baseLongEdgePixels: PlanMapRasterExporter.PublicationSheetPanelBaseLongEdge);
    }

    private static CapturedPanels CapturePanels(CaveProjectDocument project, PublicationSheetOptions options)
    {
        var quality = options.Quality;

        byte[]? elevation;
        string elevationLabel;
        if (options.ElevationSource == PublicationSheetElevationSource.Section)
        {
            elevationLabel = "Section elevation";
            elevation = CapturePlanPanel(
                project,
                SurveyVisualizationMode.Standard,
                SurveyCanvasKind.Section,
                SurveyStationGeometry.AndroidViewModeSection,
                quality);
        }
        else
        {
            elevationLabel = "Extended elevation";
            elevation = CapturePlanPanel(
                project,
                SurveyVisualizationMode.LongProfile,
                SurveyCanvasKind.Plan,
                SurveyStationGeometry.AndroidViewModePlan,
                quality);
        }

        var plan = CapturePlanPanel(
            project,
            SurveyVisualizationMode.Standard,
            SurveyCanvasKind.Plan,
            SurveyStationGeometry.AndroidViewModePlan,
            quality);

        byte[]? overview3D = null;
        if (options.Include3DOverview && (elevation != null || plan != null))
        {
            var edge = quality == MapExportQuality.Print ? 960 : 640;
            overview3D = PlanMapRasterExporter.TryCapture3DPng(
                project,
                edge,
                (int)(edge * 0.75),
                Viewport3DDisplayOptions.Clean);
        }

        return new CapturedPanels(elevation, plan, overview3D, elevationLabel);
    }

    private static Grid BuildSheetVisual(
        CaveProjectDocument project,
        CapturedPanels panels,
        PublicationSheetOptions options,
        int sheetW,
        int sheetH)
    {
        var margin = MarginDip;
        var headerH = HeaderHeightDip;
        var gap = PanelGapDip;
        var legendW = LegendWidthDip;

        var root = new Grid
        {
            Width = sheetW,
            Height = sheetH,
            Background = Brushes.White,
        };

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerH) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = BuildHeader(project, legendW);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid { Margin = new Thickness(margin, gap, margin, margin) };
        Grid.SetRow(body, 1);

        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.58, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(gap) });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.42, GridUnitType.Star) });

        var elevationPanel = BuildMapPanel(panels.ElevationLabel, panels.Elevation);
        Grid.SetRow(elevationPanel, 0);
        body.Children.Add(elevationPanel);

        var bottom = new Grid();
        Grid.SetRow(bottom, 2);
        if (options.Include3DOverview && panels.Overview3D != null)
        {
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.38, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.62, GridUnitType.Star) });

            var d3Panel = BuildMapPanel("3D overview", panels.Overview3D);
            Grid.SetColumn(d3Panel, 0);
            bottom.Children.Add(d3Panel);

            var planPanel = BuildMapPanel("Plan view", panels.Plan);
            Grid.SetColumn(planPanel, 2);
            bottom.Children.Add(planPanel);
        }
        else
        {
            var planPanel = BuildMapPanel("Plan view", panels.Plan);
            bottom.Children.Add(planPanel);
        }

        body.Children.Add(bottom);
        root.Children.Add(body);
        return root;
    }

    private static Border BuildHeader(CaveProjectDocument project, double legendWidth)
    {
        var dock = new DockPanel
        {
            Margin = new Thickness(MarginDip, MarginDip, MarginDip, 0),
            LastChildFill = true,
        };

        var legend = BuildLegendPanel(legendWidth);
        DockPanel.SetDock(legend, Dock.Left);
        dock.Children.Add(legend);

        var titleBlock = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var caveName = CaveProjectDisplayNames.GetDisplayName(project);
        if (string.IsNullOrWhiteSpace(caveName))
            caveName = "Unnamed cave";

        titleBlock.Children.Add(new TextBlock
        {
            Text = caveName,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
        });

        var site = SurveySiteTypeResolver.GetMapLabel(project);
        var dateLine = string.IsNullOrWhiteSpace(project.Date) ? "-" : project.Date.Trim();
        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(site))
            metaParts.Add(site.Trim());
        metaParts.Add(dateLine);
        metaParts.Add("Publication sheet");
        titleBlock.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", metaParts),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Margin = new Thickness(0, 4, 0, 0),
        });

        var stats = PublicationSheetStats.TryFromProject(project);
        if (stats != null)
        {
            var inv = CultureInfo.InvariantCulture;
            var statsLine =
                $"Length {stats.SurveyLengthMetres.ToString("0.#", inv)} m" +
                $"  ·  Vertical span {stats.VerticalSpanMetres.ToString("0.#", inv)} m" +
                $"  ·  Stations {stats.StationCount}" +
                $"  ·  Legs {stats.TraverseLegCount}";
            titleBlock.Children.Add(new TextBlock
            {
                Text = statsLine,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        titleBlock.Children.Add(new TextBlock
        {
            Text = "CAVE AI PRO — composite survey presentation",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            FontStyle = FontStyles.Italic,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            Margin = new Thickness(0, 8, 0, 0),
        });

        dock.Children.Add(titleBlock);
        return new Border { Child = dock };
    }

    private static Border BuildLegendPanel(double width)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Width = width };
        panel.Children.Add(new TextBlock
        {
            Text = "Legend",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 6),
        });

        const double symbolSize = 14;
        foreach (var kind in CaveMappingSymbolCatalog.DefaultLegendKinds)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 3),
            };
            var path = CaveMappingSymbolCatalog.CreatePath(
                kind,
                symbolSize,
                new SolidColorBrush(Color.FromArgb(0x66, 0x45, 0xA8, 0x9A)),
                new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x38)),
                1.0);
            path.Width = symbolSize;
            path.Height = symbolSize;
            path.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(path);
            row.Children.Add(new TextBlock
            {
                Text = CaveMappingSymbolCatalog.GetLabel(kind),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9.5,
                Foreground = Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = width - symbolSize - 8,
            });
            panel.Children.Add(row);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)),
            Child = panel,
        };
    }

    private static Border BuildMapPanel(string title, byte[]? pngBytes)
    {
        var content = new Grid { Background = Brushes.White };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        content.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.Black,
            Margin = new Thickness(8, 6, 8, 4),
        });

        var host = new Grid { Background = Brushes.White };
        Grid.SetRow(host, 1);
        if (pngBytes is { Length: > 0 })
        {
            var img = new Image
            {
                Source = LoadPng(pngBytes),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6),
            };
            host.Children.Add(img);
        }
        else
        {
            host.Children.Add(new TextBlock
            {
                Text = "Not available for this project.",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        content.Children.Add(host);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private static BitmapSource LoadPng(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private static byte[]? RenderToPng(Visual visual, int logicalW, int logicalH, int outputW, int outputH)
    {
        RenderTargetBitmap rtb;
        if (logicalW == outputW && logicalH == outputH)
        {
            rtb = new RenderTargetBitmap(outputW, outputH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
        }
        else
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new VisualBrush(visual)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, logicalW, logicalH),
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, outputW, outputH));
            }

            rtb = new RenderTargetBitmap(outputW, outputH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
        }

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
