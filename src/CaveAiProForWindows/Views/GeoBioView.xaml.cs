using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

/// <summary>
/// GEO &amp; BIO offline reader. Splits Android scientific records (rocks, fieldCatalogEntries, geoBioRecords)
/// into Geology and Biology sub-tabs, with a full field-catalog analytical table aligned to Android
/// <c>ProjectFieldCatalogEntry</c> (biota, bacteria, plants, minerals, anthropology, etc.).
/// </summary>
public partial class GeoBioView : UserControl
{
    private sealed record KindFilterOption(string Label, FieldCatalogEntryKind? Kind);

    private IReadOnlyList<GeoBioRecord> _allRecords = [];
    private List<FieldCatalogTableRowViewModel> _catalogRows = [];

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(GeoBioView),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(GeoBioView),
        new PropertyMetadata(null, OnInputChanged));

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    public GeoBioView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GeoBioView v && !Equals(e.NewValue, e.OldValue))
            v.Refresh();
    }

    private void Refresh()
    {
        GeologyItemsControl.ItemsSource = null;
        BiologyItemsControl.ItemsSource = null;
        CatalogDataGrid.ItemsSource = null;
        GeologyEmptyBorder.Visibility = Visibility.Collapsed;
        BiologyEmptyBorder.Visibility = Visibility.Collapsed;
        CatalogEmptyBorder.Visibility = Visibility.Collapsed;
        GlobalAnalysisBorder.Visibility = Visibility.Collapsed;
        GlobalAnalysisText.Text = "";
        CatalogSummaryText.Text = "";
        PendingGeoBorder.Visibility = Visibility.Collapsed;
        PendingRocksList.ItemsSource = null;

        var project = Project;
        if (project == null)
        {
            OfflineHintText.Text = "";
            ShowEmpty(GeologyEmptyBorder, GeologyEmptyText, "Select a cave project.");
            ShowEmpty(BiologyEmptyBorder, BiologyEmptyText, "Select a cave project.");
            ShowCatalogEmpty("Select a cave project.");
            return;
        }

        var globalAnalysis = AndroidBackupImageDiscovery.FindGeologyAnalysisText(project, ZipPath);
        if (!string.IsNullOrWhiteSpace(globalAnalysis))
        {
            GlobalAnalysisText.Text = globalAnalysis.Trim();
            GlobalAnalysisBorder.Visibility = Visibility.Visible;
        }

        _allRecords = GeoBioRecordsService.Build(project);
        CatalogSummaryText.Text = FieldCatalogAnalyticsFormatter.BuildSummaryLine(_allRecords);
        OfflineHintText.Text = CaveAiOfflineBrain.FormatStatusHintPanel(project);
        RefreshPendingGeoPanel(project);

        var (geologyVms, biologyVms) = BuildViewModels(project, _allRecords);

        GeologyItemsControl.ItemsSource = geologyVms.Count > 0 ? geologyVms : null;
        BiologyItemsControl.ItemsSource = biologyVms.Count > 0 ? biologyVms : null;

        SubtitleText.Text =
            $"{_allRecords.Count} record(s) — {geologyVms.Count} geology cards, {biologyVms.Count} biology cards. " +
            "Use CATALOG TABLE for the full Android field log (organisms, fungi, bacteria, plants, minerals).";

        if (geologyVms.Count == 0)
        {
            var hint = string.IsNullOrWhiteSpace(globalAnalysis)
                ? "No rocks/mineral records in this backup. Add Geo/Bio samples or field catalog rows on Android, then re-export."
                : "No itemized rock records — global Cave AI analysis is shown above.";
            ShowEmpty(GeologyEmptyBorder, GeologyEmptyText, hint);
        }

        if (biologyVms.Count == 0)
        {
            ShowEmpty(BiologyEmptyBorder, BiologyEmptyText,
                "No biological rows yet. On Android use Field Catalog kinds BIOTA, BACTERIA, PLANT (bats, fungi, invertebrates, etc.) then re-export the backup.");
        }

        RefreshCatalogTable();
    }

    private void RefreshPendingGeoPanel(CaveProjectDocument project)
    {
        var pending = ListPendingRockSamples(project);
        if (pending.Count == 0)
        {
            PendingGeoBorder.Visibility = Visibility.Collapsed;
            return;
        }

        PendingGeoTitle.Text = $"{pending.Count} pending geo/bio sample(s) — sync on Android";
        PendingRocksList.ItemsSource = pending;
        PendingGeoReminder.Text =
            "These rocks have photos but no cloud analysis yet. On Android: open GEO/BIO → sync pending samples, then re-export the backup ZIP to this PC.";
        PendingGeoBorder.Visibility = Visibility.Visible;
    }

    private static List<string> ListPendingRockSamples(CaveProjectDocument project)
    {
        var list = new List<string>();
        if (project.Rocks is not System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } rocks)
            return list;

        foreach (var rock in rocks.EnumerateArray())
        {
            var imageUri = rock.TryGetProperty("imageUri", out var img) ? img.GetString() : null;
            var isAnalyzed = rock.TryGetProperty("isAnalyzed", out var analyzed) &&
                             analyzed.ValueKind == System.Text.Json.JsonValueKind.True;
            if (string.IsNullOrWhiteSpace(imageUri) || isAnalyzed)
                continue;

            var label = rock.TryGetProperty("label", out var lbl) ? lbl.GetString()
                : rock.TryGetProperty("name", out var nm) ? nm.GetString()
                : rock.TryGetProperty("station", out var st) ? st.GetString()
                : null;
            list.Add(string.IsNullOrWhiteSpace(label) ? "(unnamed sample)" : label.Trim());
        }

        return list;
    }

    private void RefreshCatalogTable()
    {
        _catalogRows = _allRecords
            .Where(r => r.IsFieldCatalogEntry)
            .Select(r => new FieldCatalogTableRowViewModel(r))
            .ToList();

        if (_catalogRows.Count == 0)
        {
            CatalogDataGrid.Visibility = Visibility.Collapsed;
            ShowCatalogEmpty(
                "No fieldCatalogEntries[] in this project. On Android open Field Catalog for this cave, log organisms/minerals, then Save ZIP backup.");
            return;
        }

        CatalogDataGrid.Visibility = Visibility.Visible;
        CatalogEmptyBorder.Visibility = Visibility.Collapsed;
        EnsureCatalogFilterCombo();
        ApplyCatalogFilter();
    }

    private void EnsureCatalogFilterCombo()
    {
        if (CatalogKindFilterCombo.Items.Count > 0)
            return;

        var options = new List<KindFilterOption> { new("All kinds", null) };
        foreach (var kind in new[]
                 {
                     FieldCatalogEntryKind.Mineral,
                     FieldCatalogEntryKind.Biota,
                     FieldCatalogEntryKind.Bacteria,
                     FieldCatalogEntryKind.Plant,
                     FieldCatalogEntryKind.Anthropology,
                     FieldCatalogEntryKind.Depth,
                     FieldCatalogEntryKind.SurveyLength,
                     FieldCatalogEntryKind.Model3D,
                     FieldCatalogEntryKind.GeneralNote,
                     FieldCatalogEntryKind.Unknown,
                 })
        {
            options.Add(new KindFilterOption(FieldCatalogEntryKindMapper.DisplayLabel(kind), kind));
        }

        CatalogKindFilterCombo.ItemsSource = options;
        CatalogKindFilterCombo.DisplayMemberPath = nameof(KindFilterOption.Label);
        CatalogKindFilterCombo.SelectedIndex = 0;
    }

    private void ApplyCatalogFilter()
    {
        if (_catalogRows.Count == 0)
            return;

        var selected = CatalogKindFilterCombo.SelectedItem as KindFilterOption;
        IEnumerable<FieldCatalogTableRowViewModel> rows = _catalogRows;
        if (selected?.Kind is { } kind)
        {
            rows = _catalogRows.Where(r =>
            {
                var rec = _allRecords.FirstOrDefault(x =>
                    x.IsFieldCatalogEntry && x.SourceLabel == r.Source);
                return rec?.FieldKind == kind;
            });
        }

        CatalogDataGrid.ItemsSource = rows.ToList();
    }

    private void CatalogKindFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _catalogRows.Count == 0)
            return;
        ApplyCatalogFilter();
    }

    private (List<GeoBioRecordCardViewModel> geo, List<GeoBioRecordCardViewModel> bio) BuildViewModels(
        CaveProjectDocument project,
        IReadOnlyList<GeoBioRecord> records)
    {
        var geo = new List<GeoBioRecordCardViewModel>();
        var bio = new List<GeoBioRecordCardViewModel>();
        foreach (var r in records)
        {
            var images = ScientificImageLoader.Load(r, project, ZipPath);
            var vm = GeoBioRecordCardViewModel.From(r, images);
            switch (r.Category)
            {
                case GeoBioCategory.Rock:
                    geo.Add(vm);
                    break;
                case GeoBioCategory.Organism:
                    bio.Add(vm);
                    break;
                case GeoBioCategory.Mixed:
                    geo.Add(vm);
                    bio.Add(vm);
                    break;
                default:
                    if (r.FieldKind is FieldCatalogEntryKind.Biota or FieldCatalogEntryKind.Bacteria
                        or FieldCatalogEntryKind.Plant or FieldCatalogEntryKind.Anthropology)
                        bio.Add(vm);
                    else if (r.FieldKind == FieldCatalogEntryKind.Mineral)
                        geo.Add(vm);
                    else
                        geo.Add(vm);
                    break;
            }
        }

        return (geo, bio);
    }

    private void ShowCatalogEmpty(string message)
    {
        CatalogEmptyText.Text = message;
        CatalogEmptyBorder.Visibility = Visibility.Visible;
    }

    private static void ShowEmpty(Border border, TextBlock text, string message)
    {
        text.Text = message;
        border.Visibility = Visibility.Visible;
    }

    private void HeroImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is GeoBioRecordCardViewModel vm && vm.HeroImage is { } bmp)
            OpenLightBox(bmp, $"{vm.Title} — hero photo");
    }

    private void ExtraImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is GeoBioImageViewModel img)
            OpenLightBox(img.Bitmap, img.Caption);
    }

    private void OpenLightBox(BitmapSource bmp, string caption)
    {
        var owner = Window.GetWindow(this);
        ImageLightBoxWindow.Show(bmp, caption, owner);
    }
}
