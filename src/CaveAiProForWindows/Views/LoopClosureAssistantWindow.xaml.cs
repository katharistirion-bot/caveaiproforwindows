using System.Globalization;
using System.IO;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.SurveyAnalysis;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class LoopClosureAssistantWindow : Window
{
    private readonly CaveProjectDocument _project;
    private SurveyLoopAdjustmentResult? _lastPreview;

    public LoopClosureAssistantWindow(CaveProjectDocument project)
    {
        _project = project;
        InitializeComponent();
        var inv = CultureInfo.InvariantCulture;
        var loops = SurveyLoopClosureAdjuster.DetectLoops(project);
        var closing = Services.SurveyLoopClosureHighlighter.Detect(project);
        var rows = loops.Select(loop =>
        {
            var mis = loop.MisclosureMeters;
            var suggestion = mis switch
            {
                < 0.05 => "Excellent — within typical tape tolerance.",
                < 0.25 => "Acceptable — consider Compass rule if distributing error.",
                < 1.0 => "Review closing leg and backsights; Compass or WLS adjustment recommended.",
                _ => "Large misclosure — re-measure loop legs before adjustment.",
            };
            return new LoopRow
            {
                StationPath = string.Join(" → ", loop.Stations),
                SuggestedClosePoint = loop.Stations.Count > 0 ? loop.Stations[^1] : "—",
                ClosingLeg = loop.ClosingLeg,
                MisclosureMeters = mis.ToString("0.###", inv),
                TotalLegLength = loop.TotalLegLength.ToString("0.##", inv),
                Suggestion = suggestion,
            };
        }).ToList();

        if (rows.Count == 0 && closing.Count > 0)
        {
            foreach (var h in closing)
            {
                rows.Add(new LoopRow
                {
                    StationPath = $"{h.FromStation} → {h.ToStation}",
                    SuggestedClosePoint = h.ToStation,
                    ClosingLeg = h.Label,
                    MisclosureMeters = h.MisclosureMeters.ToString("0.###", inv),
                    TotalLegLength = "—",
                    Suggestion = h.MisclosureMeters < 0.25
                        ? "Loop-closing leg highlight — acceptable misclosure."
                        : "Large loop closure — verify measurements.",
                });
            }
        }

        LoopGrid.ItemsSource = rows;
        Title = $"Loop closure — {project.Name} ({rows.Count} loop(s))";
        PreviewSummaryText.Text = rows.Count == 0
            ? "No closed loops detected."
            : "Select a method and click Preview adjustment to see total misclosure before/after.";
    }

    private LoopAdjustmentMethod SelectedMethod =>
        MethodBox.SelectedIndex == 1
            ? LoopAdjustmentMethod.WeightedLeastSquares
            : LoopAdjustmentMethod.CompassRule;

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var inv = CultureInfo.InvariantCulture;
        _lastPreview = SurveyLoopClosureAdjuster.Adjust(_project, SelectedMethod);
        PreviewSummaryText.Text =
            $"Preview ({_lastPreview.Method}): total misclosure " +
            $"{_lastPreview.TotalMisclosureBefore.ToString("0.###", inv)} m → " +
            $"{_lastPreview.TotalMisclosureAfter.ToString("0.###", inv)} m " +
            $"({_lastPreview.Loops.Count} loop(s)). " +
            "Raw shots are unchanged — Apply to copy writes plan overrides only.";
    }

    private void ApplyCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_lastPreview == null)
        {
            MessageBox.Show(this,
                "Run Preview adjustment first.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var safe = string.Join("_", _project.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Title = "Save adjusted survey copy (Android backup ZIP)",
            Filter = "CaveAI backup ZIP|*.zip",
            FileName = $"CaveAI_Backup_{safe}_adjusted.zip",
            AddExtension = true,
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var copy = CaveProjectDocumentCloner.Clone(_project);
            SurveyLoopClosureAdjuster.ApplyToPlanOverrides(copy, _lastPreview);
            AndroidBackupZipExporter.ExportSingleProject(copy, dlg.FileName);
            MessageBox.Show(this,
                "Adjusted copy saved. Raw shot measurements are unchanged; plan station overrides hold the adjustment.\n\n" +
                dlg.FileName,
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class LoopRow
    {
        public string StationPath { get; init; } = "";

        public string SuggestedClosePoint { get; init; } = "";

        public string ClosingLeg { get; init; } = "";

        public string MisclosureMeters { get; init; } = "";

        public string TotalLegLength { get; init; } = "";

        public string Suggestion { get; init; } = "";
    }
}
