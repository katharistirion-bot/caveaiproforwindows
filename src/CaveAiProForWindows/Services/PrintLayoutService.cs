using System.Globalization;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Builds a single-page <see cref="FixedDocument"/> from a captured survey map,
/// adapting margins, typography, and fit-to-page map bounds to ISO paper dimensions.
/// </summary>
public static class PrintLayoutService
{
    private const double DipPerMm = 96.0 / 25.4;
    private static readonly ImageSource Logo = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
    private static readonly ImageSource MapWatermark = new BitmapImage(new Uri("pack://application:,,,/Assets/ic_launcher_logo.png"));

    public static FixedDocument Build(SurveyMapPrintRequest request, PrintLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        var metrics = GetMetrics(options.PaperSize);
        var pageSize = GetPageSize(options.PaperSize, options.Orientation);
        var margin = metrics.MarginDip;
        var contentWidth = pageSize.Width - margin * 2;

        var doc = new FixedDocument();
        doc.DocumentPaginator.PageSize = pageSize;

        var fixedPage = new FixedPage
        {
            Width = pageSize.Width,
            Height = pageSize.Height,
            Background = Brushes.White,
        };

        var y = margin;
        var headerHeight = AddHeaderPlate(fixedPage, request, margin, contentWidth, ref y, metrics);

        var mapTop = y + metrics.HeaderGap;
        var mapWidth = contentWidth;
        var mapHeight = pageSize.Height - mapTop - metrics.FooterReserve - margin;
        mapHeight = Math.Max(48, mapHeight);

        AddMapFrame(fixedPage, request, options, margin, mapTop, mapWidth, mapHeight, metrics);
        AddFooter(fixedPage, request, margin, contentWidth, pageSize.Height, metrics, options);

        var pageContent = new PageContent();
        ((System.Windows.Markup.IAddChild)pageContent).AddChild(fixedPage);
        doc.Pages.Add(pageContent);
        return doc;
    }

    public static Size GetPageSize(PrintPaperSize paper, PageOrientation orientation)
    {
        var (wMm, hMm) = paper switch
        {
            PrintPaperSize.A2 => (420.0, 594.0),
            PrintPaperSize.A3 => (297.0, 420.0),
            _ => (210.0, 297.0),
        };

        if (orientation == PageOrientation.Landscape)
            (wMm, hMm) = (hMm, wMm);

        return new Size(wMm * DipPerMm, hMm * DipPerMm);
    }

    public static PageMediaSizeName ToPageMediaSizeName(PrintPaperSize paper) =>
        paper switch
        {
            PrintPaperSize.A2 => PageMediaSizeName.ISOA2,
            PrintPaperSize.A3 => PageMediaSizeName.ISOA3,
            _ => PageMediaSizeName.ISOA4,
        };

    private static PrintLayoutMetrics GetMetrics(PrintPaperSize paper) =>
        paper switch
        {
            PrintPaperSize.A2 => new PrintLayoutMetrics(
                MarginDip: 20 * DipPerMm,
                LogoSize: 64,
                TitleFontSize: 24,
                MetaFontSize: 14,
                SummaryFontSize: 13,
                FooterFontSize: 11,
                HeaderGap: 12,
                FooterReserve: 28,
                TextColumnGap: 14),
            PrintPaperSize.A3 => new PrintLayoutMetrics(
                MarginDip: 18 * DipPerMm,
                LogoSize: 56,
                TitleFontSize: 21,
                MetaFontSize: 12.5,
                SummaryFontSize: 12,
                FooterFontSize: 10,
                HeaderGap: 10,
                FooterReserve: 24,
                TextColumnGap: 12),
            _ => new PrintLayoutMetrics(
                MarginDip: 15 * DipPerMm,
                LogoSize: 48,
                TitleFontSize: 18,
                MetaFontSize: 11.5,
                SummaryFontSize: 11,
                FooterFontSize: 9.5,
                HeaderGap: 8,
                FooterReserve: 22,
                TextColumnGap: 10),
        };

    private static double AddHeaderPlate(
        FixedPage page,
        SurveyMapPrintRequest request,
        double margin,
        double contentWidth,
        ref double y,
        PrintLayoutMetrics metrics)
    {
        var project = request.Project;
        var caveName = CaveProjectDisplayNames.GetDisplayName(project);
        if (string.IsNullOrWhiteSpace(caveName))
            caveName = "Unnamed cave";

        var textLeft = margin + metrics.LogoSize + metrics.TextColumnGap;
        var textWidth = Math.Max(48, contentWidth - metrics.LogoSize - metrics.TextColumnGap);

        var titleHeight = MeasureTextHeight(
            caveName,
            textWidth,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            metrics.TitleFontSize);

        var dateLine = string.IsNullOrWhiteSpace(project.Date) ? "—" : project.Date.Trim();
        var meta = $"{dateLine}  ·  {request.MapKindLabel}";
        if (request.HighContrast)
            meta += "  ·  High contrast";
        if (!string.IsNullOrWhiteSpace(request.SubtitleSuffix))
            meta += request.SubtitleSuffix;

        var metaHeight = MeasureTextHeight(
            meta,
            textWidth,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            metrics.MetaFontSize);

        var (_, _, _, summaryLine) = SurveyPlanHudStats.Compute(project);
        var summaryHeight = MeasureTextHeight(
            summaryLine,
            textWidth,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            metrics.SummaryFontSize);

        var metaTop = titleHeight + 4;
        var summaryTop = metaTop + metaHeight + 4;
        var textStackHeight = summaryTop + summaryHeight;
        var headerHeight = Math.Max(metrics.LogoSize, textStackHeight);

        var logo = new Image
        {
            Source = Logo,
            Width = metrics.LogoSize,
            Height = metrics.LogoSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
        };
        FixedPage.SetLeft(logo, margin);
        FixedPage.SetTop(logo, y);
        page.Children.Add(logo);

        var title = new TextBlock
        {
            Text = caveName,
            FontSize = metrics.TitleFontSize,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Width = textWidth,
        };
        FixedPage.SetLeft(title, textLeft);
        FixedPage.SetTop(title, y);
        page.Children.Add(title);

        var metaBlock = new TextBlock
        {
            Text = meta,
            FontSize = metrics.MetaFontSize,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Width = textWidth,
        };
        FixedPage.SetLeft(metaBlock, textLeft);
        FixedPage.SetTop(metaBlock, y + metaTop);
        page.Children.Add(metaBlock);

        var summary = new TextBlock
        {
            Text = summaryLine,
            FontSize = metrics.SummaryFontSize,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Width = textWidth,
        };
        FixedPage.SetLeft(summary, textLeft);
        FixedPage.SetTop(summary, y + summaryTop);
        page.Children.Add(summary);

        y += headerHeight;
        return headerHeight;
    }

    private static void AddMapFrame(
        FixedPage page,
        SurveyMapPrintRequest request,
        PrintLayoutOptions options,
        double left,
        double top,
        double boundsWidth,
        double boundsHeight,
        PrintLayoutMetrics metrics)
    {
        if (boundsWidth <= 0 || boundsHeight <= 0)
            return;

        var mapImage = request.MapImage;
        var srcW = Math.Max(1, mapImage.Width);
        var srcH = Math.Max(1, mapImage.Height);
        var scale = options.FitMapToPage
            ? Math.Min(boundsWidth / srcW, boundsHeight / srcH)
            : Math.Min(1.0, Math.Min(boundsWidth / srcW, boundsHeight / srcH));
        var drawW = srcW * scale;
        var drawH = srcH * scale;

        var mapHost = new Grid
        {
            Width = boundsWidth,
            Height = boundsHeight,
            Background = Brushes.White,
            ClipToBounds = true,
        };
        mapHost.Children.Add(new Image
        {
            Source = MapWatermark,
            Width = Math.Min(boundsWidth, boundsHeight) * 0.72,
            Height = Math.Min(boundsWidth, boundsHeight) * 0.72,
            Opacity = 0.05,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        });
        mapHost.Children.Add(new Image
        {
            Source = mapImage,
            Width = drawW,
            Height = drawH,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        });

        if (request.Cartography is { ShowCartographyOverlay: true, SourcePxPerMetre: > 0 } cartography)
        {
            var effectivePxPerMetre = cartography.SourcePxPerMetre.Value * (drawW / srcW);
            PrintCartographyOverlayBuilder.AddToMapFrame(
                mapHost,
                drawW,
                drawH,
                effectivePxPerMetre,
                cartography.CanvasKind,
                request.HighContrast);
        }

        var border = new Border
        {
            Width = boundsWidth,
            Height = boundsHeight,
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            ClipToBounds = true,
            Child = mapHost,
        };

        FixedPage.SetLeft(border, left);
        FixedPage.SetTop(border, top);
        page.Children.Add(border);
    }

    private static void AddFooter(
        FixedPage page,
        SurveyMapPrintRequest request,
        double margin,
        double contentWidth,
        double pageHeight,
        PrintLayoutMetrics metrics,
        PrintLayoutOptions options)
    {
        var inv = CultureInfo.InvariantCulture;
        var paperLabel = options.PaperSize.ToString();
        var orientLabel = options.Orientation == PageOrientation.Landscape ? "Landscape" : "Portrait";
        var scaleLabel = options.FitMapToPage ? "Fit to page" : "Actual size";
        var footer = new TextBlock
        {
            Text =
                $"CAVE AI PRO  ·  {paperLabel} {orientLabel} · {scaleLabel}  ·  Printed {DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv)}  ·  {request.MapKindLabel}",
            FontSize = metrics.FooterFontSize,
            Foreground = Brushes.Gray,
            Width = contentWidth,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        FixedPage.SetLeft(footer, margin);
        FixedPage.SetTop(footer, pageHeight - margin - metrics.FooterReserve + 4);
        page.Children.Add(footer);
    }

    private static double MeasureTextHeight(string text, double maxWidth, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            Trimming = TextTrimming.None,
        };
        return formatted.Height;
    }

    private readonly record struct PrintLayoutMetrics(
        double MarginDip,
        double LogoSize,
        double TitleFontSize,
        double MetaFontSize,
        double SummaryFontSize,
        double FooterFontSize,
        double HeaderGap,
        double FooterReserve,
        double TextColumnGap);
}
