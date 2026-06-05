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
/// into Geology and Biology sub-tabs, renders Cave AI analyses as scrollable typography, and opens any photo
/// in <see cref="ImageLightBoxWindow"/> on click.
/// </summary>
public partial class GeoBioView : UserControl
{
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
        GeologyEmptyBorder.Visibility = Visibility.Collapsed;
        BiologyEmptyBorder.Visibility = Visibility.Collapsed;
        GlobalAnalysisBorder.Visibility = Visibility.Collapsed;
        GlobalAnalysisText.Text = "";

        var project = Project;
        if (project == null)
        {
            ShowEmpty(GeologyEmptyBorder, GeologyEmptyText, "Select a cave project.");
            ShowEmpty(BiologyEmptyBorder, BiologyEmptyText, "Select a cave project.");
            return;
        }

        var globalAnalysis = AndroidBackupImageDiscovery.FindGeologyAnalysisText(project, ZipPath);
        if (!string.IsNullOrWhiteSpace(globalAnalysis))
        {
            GlobalAnalysisText.Text = globalAnalysis.Trim();
            GlobalAnalysisBorder.Visibility = Visibility.Visible;
        }

        var records = GeoBioRecordsService.Build(project);
        var (geologyVms, biologyVms) = BuildViewModels(project, records);

        GeologyItemsControl.ItemsSource = geologyVms.Count > 0 ? geologyVms : null;
        BiologyItemsControl.ItemsSource = biologyVms.Count > 0 ? biologyVms : null;

        SubtitleText.Text =
            $"{records.Count} record(s) parsed — {geologyVms.Count} geology, {biologyVms.Count} biology. Photos open in full-screen on click.";

        if (geologyVms.Count == 0)
        {
            var hint = string.IsNullOrWhiteSpace(globalAnalysis)
                ? "No rocks/mineral records or Cave AI geology analysis in this backup. Open BACKUP CONTENTS to inspect raw JSON."
                : "No itemized rock records — global Cave AI analysis is shown above.";
            ShowEmpty(GeologyEmptyBorder, GeologyEmptyText, hint);
        }

        if (biologyVms.Count == 0)
        {
            ShowEmpty(BiologyEmptyBorder, BiologyEmptyText,
                "No biological observations recorded. Tag records with category=\"organism\" / \"flora\" / \"fauna\" on Android (or use the new geoBioRecords export) to populate this tab.");
        }
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
                    // "Other / unclassified" — bucket alongside geology so the user still sees them.
                    geo.Add(vm);
                    break;
            }
        }

        return (geo, bio);
    }

    private static void ShowEmpty(System.Windows.Controls.Border border, TextBlock text, string message)
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
