using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.ViewModels;

/// <summary>One bound row in the GEO &amp; BIO ItemsControl: title, category badge, station, analysis, photos.</summary>
public sealed class GeoBioRecordCardViewModel
{
    private GeoBioRecordCardViewModel(
        string title,
        string categoryLabel,
        string stationLabel,
        string sourceLabel,
        string? analysisText,
        string? detailsSummary,
        BitmapSource? heroImage,
        IReadOnlyList<GeoBioImageViewModel> extraImages)
    {
        Title = title;
        CategoryLabel = categoryLabel;
        StationLabel = stationLabel;
        SourceLabel = sourceLabel;
        AnalysisText = analysisText ?? "";
        DetailsSummary = detailsSummary ?? "";
        HeroImage = heroImage;
        ExtraImages = extraImages;
    }

    public string Title { get; }
    public string CategoryLabel { get; }
    public string StationLabel { get; }
    public string SourceLabel { get; }
    public string AnalysisText { get; }
    public string DetailsSummary { get; }
    public BitmapSource? HeroImage { get; }
    public IReadOnlyList<GeoBioImageViewModel> ExtraImages { get; }

    public Visibility HeroImageVisibility => HeroImage != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoImageVisibility => HeroImage == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExtraImagesVisibility => ExtraImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnalysisVisibility =>
        string.IsNullOrWhiteSpace(AnalysisText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailsVisibility =>
        string.IsNullOrWhiteSpace(DetailsSummary) || !string.IsNullOrWhiteSpace(AnalysisText)
            ? Visibility.Collapsed
            : Visibility.Visible;

    public static GeoBioRecordCardViewModel From(GeoBioRecord record, IReadOnlyList<ScientificImageLoader.ResolvedImage> images)
    {
        var hero = images.Count > 0 ? images[0].Bitmap : null;
        var extras = images.Skip(1)
            .Select(i => new GeoBioImageViewModel(i.Bitmap, i.Caption))
            .ToList();

        return new GeoBioRecordCardViewModel(
            record.Title,
            FormatCategory(record.Category),
            FormatStation(record),
            record.SourceLabel,
            record.CaveAiAnalysisText,
            record.DetailsSummary,
            hero,
            extras);
    }

    private static string FormatCategory(GeoBioCategory c) => c switch
    {
        GeoBioCategory.Rock => "GEOLOGY",
        GeoBioCategory.Organism => "BIOLOGY",
        GeoBioCategory.Mixed => "MIXED",
        _ => "OTHER",
    };

    private static string FormatStation(GeoBioRecord r)
    {
        if (!string.IsNullOrWhiteSpace(r.Station) && !string.IsNullOrWhiteSpace(r.CoordinatesSummary))
            return $"@ {r.Station} · {r.CoordinatesSummary}";
        if (!string.IsNullOrWhiteSpace(r.Station))
            return $"@ {r.Station}";
        if (!string.IsNullOrWhiteSpace(r.CoordinatesSummary))
            return r.CoordinatesSummary!;
        return "";
    }
}

/// <summary>Bound photo thumbnail in the gallery section of a record card.</summary>
public sealed class GeoBioImageViewModel
{
    public GeoBioImageViewModel(BitmapSource bitmap, string caption)
    {
        Bitmap = bitmap;
        Caption = caption;
    }

    public BitmapSource Bitmap { get; }
    public string Caption { get; }
}
