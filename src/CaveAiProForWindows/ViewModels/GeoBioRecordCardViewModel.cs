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
        string? structuredFacts,
        string? detailsSummary,
        BitmapSource? heroImage,
        IReadOnlyList<GeoBioImageViewModel> extraImages)
    {
        Title = title;
        CategoryLabel = categoryLabel;
        StationLabel = stationLabel;
        SourceLabel = sourceLabel;
        AnalysisText = analysisText ?? "";
        StructuredFacts = structuredFacts ?? "";
        DetailsSummary = detailsSummary ?? "";
        HeroImage = heroImage;
        ExtraImages = extraImages;
    }

    public string Title { get; }
    public string CategoryLabel { get; }
    public string StationLabel { get; }
    public string SourceLabel { get; }
    public string AnalysisText { get; }
    public string StructuredFacts { get; }
    public string DetailsSummary { get; }
    public BitmapSource? HeroImage { get; }
    public IReadOnlyList<GeoBioImageViewModel> ExtraImages { get; }

    public Visibility HeroImageVisibility => HeroImage != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoImageVisibility => HeroImage == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExtraImagesVisibility => ExtraImages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnalysisVisibility =>
        string.IsNullOrWhiteSpace(AnalysisText) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StructuredFactsVisibility =>
        string.IsNullOrWhiteSpace(StructuredFacts) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailsVisibility =>
        string.IsNullOrWhiteSpace(DetailsSummary) ||
        !string.IsNullOrWhiteSpace(AnalysisText) ||
        !string.IsNullOrWhiteSpace(StructuredFacts)
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
            FormatCategory(record),
            FormatStation(record),
            record.SourceLabel,
            record.CaveAiAnalysisText,
            FormatStructuredFacts(record),
            record.DetailsSummary,
            hero,
            extras);
    }

    private static string FormatStructuredFacts(GeoBioRecord r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.ScientificName))
            parts.Add($"Scientific name: {r.ScientificName.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.TaxonomicGroup))
            parts.Add($"Group: {r.TaxonomicGroup.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Abundance))
            parts.Add($"Abundance: {r.Abundance.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.LifeStage))
            parts.Add($"Life stage: {r.LifeStage.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Microhabitat))
            parts.Add($"Microhabitat: {r.Microhabitat.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.Substrate))
            parts.Add($"Substrate: {r.Substrate.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.IdConfidence))
            parts.Add($"ID confidence: {r.IdConfidence.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.LocationDetail))
            parts.Add($"Location: {r.LocationDetail.Trim()}");
        if (!string.IsNullOrWhiteSpace(r.BehaviorNotes))
            parts.Add($"Behavior: {r.BehaviorNotes.Trim()}");
        return parts.Count == 0 ? "" : string.Join("\n", parts);
    }

    private static string FormatCategory(GeoBioRecord r)
    {
        if (r.FieldKind is { } fk)
            return FieldCatalogEntryKindMapper.DisplayLabel(fk).ToUpperInvariant();
        return FormatCategory(r.Category);
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
