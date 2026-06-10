using System.Globalization;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class SurvexExportOptionsWindow : Window
{
    public SurvexExportOptions? Result { get; private set; }

    public SurvexExportOptionsWindow(CaveProjectDocument project, Window? owner)
    {
        InitializeComponent();
        Owner = owner;
        Title = $"Survex export — {project.Name}";

        var defaults = SurvexExporter.ResolveDefaultOptions(project);
        FixStationBox.Text = defaults.FixStation;
        FixEastingBox.Text = defaults.FixEasting.ToString("0.###", CultureInfo.InvariantCulture);
        FixNorthingBox.Text = defaults.FixNorthing.ToString("0.###", CultureInfo.InvariantCulture);
        FixElevationBox.Text = defaults.FixElevation.ToString("0.###", CultureInfo.InvariantCulture);
        DeclinationBox.Text = defaults.DeclinationDeg?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
        IncludeSplaysCheck.IsChecked = defaults.IncludeSplays;
        ProvisionalFixCheck.IsChecked = defaults.ProvisionalFix;
        IncludeLrudPassageCheck.IsChecked = defaults.IncludeLrudPassage;
        IncludeEntranceGpsCsCheck.IsChecked = defaults.IncludeEntranceGpsCs;

        var splayCount = project.Shots.Count(s => !s.IsTraverseLeg && s.Distance > 0);
        SplayHintText.Text = splayCount > 0
            ? $"{splayCount} splay leg(s) will be exported in a second *data block."
            : "No splay legs in this project.";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseOptions(out var options))
            return;

        Result = options;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool TryParseOptions(out SurvexExportOptions options)
    {
        options = null!;
        var station = FixStationBox.Text.Trim();
        if (string.IsNullOrEmpty(station))
        {
            MessageBox.Show(this, "Fix station name is required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!TryParseDouble(FixEastingBox.Text, out var east) ||
            !TryParseDouble(FixNorthingBox.Text, out var north) ||
            !TryParseDouble(FixElevationBox.Text, out var elev))
        {
            MessageBox.Show(this, "Fix coordinates must be valid numbers (use . as decimal separator).",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        double? declination = null;
        if (!string.IsNullOrWhiteSpace(DeclinationBox.Text))
        {
            if (!TryParseDouble(DeclinationBox.Text, out var dec))
            {
                MessageBox.Show(this, "Declination must be a valid number or left blank.",
                    Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            declination = dec;
        }

        options = new SurvexExportOptions
        {
            FixStation = station,
            FixEasting = east,
            FixNorthing = north,
            FixElevation = elev,
            DeclinationDeg = declination,
            IncludeSplays = IncludeSplaysCheck.IsChecked == true,
            ProvisionalFix = ProvisionalFixCheck.IsChecked == true,
            IncludeLrudPassage = IncludeLrudPassageCheck.IsChecked == true,
            IncludeEntranceGpsCs = IncludeEntranceGpsCsCheck.IsChecked == true,
        };
        return true;
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
