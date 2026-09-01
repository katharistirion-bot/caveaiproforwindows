using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.ReferenceCatalog;

namespace CaveAiProForWindows.Views;

public partial class ReferenceCommunityCompareWindow : Window
{
    private IReadOnlyList<ReferenceCaveIndexEntry> _index = [];
    private ReferenceCaveIndexEntry? _prefillRef;
    private CaveProjectDocument? _surveyLinkProject;

    public ReferenceCommunityCompareWindow()
    {
        InitializeComponent();
        RefSearchA.TextChanged += (_, _) => ApplyRefFilter(RefSearchA, RefListA);
        RefSearchB.TextChanged += (_, _) => ApplyRefFilter(RefSearchB, RefListB);
        Loaded += async (_, _) => await LoadIndexAsync();
    }

    public static void ShowDialog(Window? owner, ReferenceCaveIndexEntry? prefillReference = null)
    {
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.ReferenceCompareOpen);
        var win = new ReferenceCommunityCompareWindow { Owner = owner, _prefillRef = prefillReference };
        win.ShowDialog();
    }

    public static void ShowSurveyLinkCompare(Window? owner, CaveProjectDocument project)
    {
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.SurveyReferenceCompare);
        var win = new ReferenceCommunityCompareWindow { Owner = owner, _surveyLinkProject = project };
        win.ShowDialog();
    }

    private async Task LoadIndexAsync()
    {
        try
        {
            var fetch = new ReferenceCatalogFetchService();
            var state = await fetch.LoadBrowseIndexAsync();
            _index = state.IndexEntries;
            RefListA.ItemsSource = _index.Take(200).ToList();
            RefListB.ItemsSource = _index.Take(200).ToList();

            var surveys = CollectSurveyChoices();
            SurveyComboA.ItemsSource = surveys;
            SurveyComboB.ItemsSource = surveys;
            SurveyComboB.SelectionChanged += SurveyComboB_SelectionChanged;

            if (_prefillRef != null)
            {
                RefListA.SelectedItem = _index.FirstOrDefault(e => e.Id == _prefillRef.Id) ?? _prefillRef;
                RefSearchA.Text = _prefillRef.Name;
            }

            if (_surveyLinkProject != null)
                RunSurveyLinkCompare(_surveyLinkProject);
        }
        catch (Exception ex)
        {
            ResultText.Text = Services.Auth.GuestLibraryCopy.CatalogLoadFailure(ex);
        }
    }

    private static List<string> CollectSurveyChoices()
    {
        var list = new List<string> { "(none)" };
        if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            foreach (var p in vm.Projects)
                list.Add(p.Name);
        }

        return list;
    }

    private void ApplyRefFilter(TextBox box, ListBox list)
    {
        if (_index.Count == 0)
            return;
        var filtered = ReferenceCatalogSearch.Filter(_index, box.Text, null, null, null, maxResults: 200);
        list.ItemsSource = filtered;
    }

    private void RefListA_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SurveyComboA.SelectedIndex = 0;
        UpdatePlanPreview();
    }

    private void RefListB_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SurveyComboB.SelectedIndex = 0;
        UpdatePlanPreview();
    }

    private void SurveyComboB_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePlanPreview();

    private void SurveyComboA_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePlanPreview();

    private void AutoLinkActiveProject_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is not ViewModels.MainViewModel vm ||
            vm.SelectedProject == null)
        {
            MessageBox.Show(this, "Select a cave project in the main window first.", "Auto-link",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_index.Count == 0)
        {
            MessageBox.Show(this, "Reference index not loaded yet.", "Auto-link",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReferenceSurveyLinkPrompt.TryPromptForProjects(this, [vm.SelectedProject], _index);
        if (ReferenceSurveyLinkService.TryGetLink(vm.SelectedProject, out var link) && link != null)
        {
            var entry = _index.FirstOrDefault(e => string.Equals(e.Id, link.Id, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                RefListA.SelectedItem = entry;
                RefSearchA.Text = entry.Name;
            }

            RunSurveyLinkCompare(vm.SelectedProject);
        }
    }

    private void UpdatePlanPreview()
    {
        PlanPreviewImage.Source = TryCapturePlanPreview(RefListA, SurveyComboA);
        PlanPreviewImageB.Source = TryCapturePlanPreview(RefListB, SurveyComboB);

        if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm &&
            vm.SelectedProject != null &&
            ReferenceSurveyLinkService.TryGetLink(vm.SelectedProject, out var link) &&
            link != null &&
            PlanPreviewImageB.Source == null)
        {
            PlanPreviewImageB.Source = TryCaptureProjectPlan(vm.SelectedProject);
        }
    }

    private static BitmapImage? TryCapturePlanPreview(ListBox refList, ComboBox surveyCombo)
    {
        if (surveyCombo.SelectedItem is string name && name != "(none)" &&
            Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            var project = vm.Projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (project != null)
                return TryCaptureProjectPlan(project);
        }

        return null;
    }

    private static BitmapImage? TryCaptureProjectPlan(CaveProjectDocument project)
    {
        var png = PlanMapRasterExporter.TryCapturePlanPngHighRes(
            project,
            SurveyVisualizationMode.Standard,
            highContrast: false,
            PlanCanvasDrawOptionsFactory.ForExport(SurveyCanvasKind.Plan),
            Array.Empty<PlanRasterUnderlay>(),
            zipPath: null,
            quality: MapExportQuality.Standard);
        return png is { Length: > 0 } ? LoadPreview(png) : null;
    }

    private static BitmapImage LoadPreview(byte[] png)
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

    private void CompareActiveProjectLink_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow?.DataContext is not ViewModels.MainViewModel vm ||
            vm.SelectedProject == null)
        {
            MessageBox.Show(this, "Select a cave project in the main window first.", "Survey vs reference",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!ReferenceSurveyLinkService.TryGetLink(vm.SelectedProject, out _))
        {
            MessageBox.Show(this,
                "The selected project has no referenceCatalogLink. Link it from Reference catalog or import a backup that includes the link.",
                "Survey vs reference", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        RunSurveyLinkCompare(vm.SelectedProject);
    }

    private void RunSurveyLinkCompare(CaveProjectDocument project)
    {
        ReferenceCaveIndexEntry? entry = null;
        if (ReferenceSurveyLinkService.TryGetLink(project, out var link) && link != null)
            entry = _index.FirstOrDefault(e => string.Equals(e.Id, link.Id, StringComparison.OrdinalIgnoreCase));

        if (ReferenceSurveyCompareService.TryBuildReport(project, out var report, entry))
        {
            ResultText.Text = report;
            if (PlanPreviewImage.Source == null)
                PlanPreviewImage.Source = TryCaptureProjectPlan(project);
            UpdatePlanPreview();
        }
        else
            ResultText.Text = "Could not build survey vs reference report for this project.";
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var sideA = ResolveSide(RefListA, SurveyComboA);
        var sideB = ResolveSide(RefListB, SurveyComboB);
        if (sideA == null || sideB == null)
        {
            MessageBox.Show(this, "Select a reference entry or published survey on each side.", "Compare", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("REFERENCE vs COMMUNITY COMPARE");
        sb.AppendLine(new string('─', 48));
        AppendSide(sb, "A", sideA);
        sb.AppendLine();
        AppendSide(sb, "B", sideB);
        sb.AppendLine();

        if (sideA.Lat.HasValue && sideA.Lon.HasValue && sideB.Lat.HasValue && sideB.Lon.HasValue)
        {
            var dist = GeoHaversine.DistanceKm(sideA.Lat.Value, sideA.Lon.Value, sideB.Lat.Value, sideB.Lon.Value);
            sb.AppendLine($"Distance A↔B: {dist:0.###} km");
        }

        if (sideA.DepthM.HasValue && sideB.DepthM.HasValue)
            sb.AppendLine($"Depth delta: {Math.Abs(sideA.DepthM.Value - sideB.DepthM.Value):0.#} m");

        if (sideA.LengthM.HasValue && sideB.LengthM.HasValue)
            sb.AppendLine($"Length delta: {Math.Abs(sideA.LengthM.Value - sideB.LengthM.Value):0.#} m");

        ResultText.Text = sb.ToString();
    }

    private CompareSide? ResolveSide(ListBox refList, ComboBox surveyCombo)
    {
        if (refList.SelectedItem is ReferenceCaveIndexEntry entry)
        {
            return new CompareSide
            {
                Label = entry.Name,
                Kind = "Reference",
                Country = entry.Country,
                Lat = entry.Lat,
                Lon = entry.Lon,
                DepthM = entry.DepthM,
                LengthM = entry.LengthM,
                Badge = entry.Rich ? "Surveyed" : "Sparse",
            };
        }

        if (surveyCombo.SelectedItem is string name && name != "(none)" &&
            Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            var project = vm.Projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (project == null)
                return null;

            return new CompareSide
            {
                Label = project.Name,
                Kind = "Published survey",
                Country = ReferenceSurveyLinkService.TryGetLink(project, out var link) ? link?.Country : null,
                Lat = project.Lat,
                Lon = project.Lon,
                DepthM = null,
                LengthM = null,
                Badge = string.IsNullOrWhiteSpace(project.LinkedLibraryCaveId) ? "Local" : "Linked",
            };
        }

        return null;
    }


    private void CompareOnExploreTerrain_Click(object sender, RoutedEventArgs e)
    {
        if (RefListA.SelectedItem is not ReferenceCaveIndexEntry left ||
            RefListB.SelectedItem is not ReferenceCaveIndexEntry right)
        {
            MessageBox.Show(this,
                "Select a reference catalog entry on both sides to frame them on Explore terrain.",
                "Compare on Explore terrain",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ReferenceCatalogGeolocation.IsValidCoordinate(left.Lat, left.Lon) ||
            !ReferenceCatalogGeolocation.IsValidCoordinate(right.Lat, right.Lon))
        {
            MessageBox.Show(this, "Both reference caves need valid GPS coordinates.", "Compare on Explore terrain",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var url = PublicLibraryCatalog.WithEmbed(ReferenceCatalogShareUrls.BuildExploreCompareTerrainUrl(left, right));
        ReferenceCatalogLightAnalytics.Increment(ReferenceCatalogLightAnalytics.Events.ReferenceCompareExploreTerrainLink);
        PublicLibraryCatalog.ShowInAppWindow(Owner, url);
    }

    private static void AppendSide(StringBuilder sb, string tag, CompareSide side)
    {
        sb.AppendLine($"[{tag}] {side.Kind}: {side.Label}");
        if (!string.IsNullOrWhiteSpace(side.Country)) sb.AppendLine($"  Country: {side.Country}");
        if (side.Lat is double la && side.Lon is double lo) sb.AppendLine($"  Coords: {la:F5}, {lo:F5}");
        if (side.DepthM is > 0) sb.AppendLine($"  Depth: {side.DepthM:0.#} m");
        if (side.LengthM is > 0) sb.AppendLine($"  Length: {side.LengthM:0.#} m");
        sb.AppendLine($"  Badge: {side.Badge}");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class CompareSide
    {
        public string Label { get; init; } = "";
        public string Kind { get; init; } = "";
        public string? Country { get; init; }
        public double? Lat { get; init; }
        public double? Lon { get; init; }
        public double? DepthM { get; init; }
        public double? LengthM { get; init; }
        public string Badge { get; init; } = "";
    }
}
