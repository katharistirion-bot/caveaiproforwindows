using System.Globalization;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.SurveyAnalysis;

namespace CaveAiProForWindows.Views;

public partial class LoopClosureAssistantWindow : Window
{
    public LoopClosureAssistantWindow(CaveProjectDocument project)
    {
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
