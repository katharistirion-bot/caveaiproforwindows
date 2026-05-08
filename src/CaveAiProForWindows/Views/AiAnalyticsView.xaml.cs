using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Offline AI analytics dashboard: local algorithms on <see cref="CaveProjectDocument"/>.</summary>
public partial class AiAnalyticsView : UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(AiAnalyticsView),
        new PropertyMetadata(null, OnProjectPropertyChanged));

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public AiAnalyticsView()
    {
        InitializeComponent();
    }

    private static void OnProjectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AiAnalyticsView v)
            return;
        if (Equals(e.NewValue, e.OldValue))
            return;
        v.ClearResultsUi("Project changed — run analysis again when ready.");
    }

    private void ClearResultsUi(string statusMessage)
    {
        if (AnalysisStatusText != null)
            AnalysisStatusText.Text = statusMessage;
        if (ResultsSummaryText != null)
            ResultsSummaryText.Text = "";
        if (ResultsDataGrid != null)
            ResultsDataGrid.ItemsSource = null;
    }

    private string? GetSelectedToolTag()
    {
        if (ModelListBox.SelectedItem is ListBoxItem li && li.Tag is string s)
            return s;
        return (ModelListBox.SelectedItem as ListBoxItem)?.Tag as string;
    }

    private async void RunAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        if (RunAnalysisButton == null || RootInteractionGrid == null)
            return;

        var tag = GetSelectedToolTag() ?? "Qc";
        var project = Project;
        RunAnalysisButton.IsEnabled = false;
        ModelListBox.IsEnabled = false;
        AnalysisProgressBar.Visibility = Visibility.Visible;
        AnalysisStatusText.Text = "Computing on loaded survey (local)…";

        try
        {
            var rows = await System.Threading.Tasks.Task.Run(() =>
                    AiLocalSurveyAnalytics.BuildResultRows(tag, project).ToList())
                .ConfigureAwait(true);

            ResultsDataGrid.ItemsSource = rows;
            if (project != null)
            {
                var name = string.IsNullOrWhiteSpace(project.Name) ? "(unnamed project)" : project.Name.Trim();
                var mode = tag switch
                {
                    "Volume" => "Volumetric",
                    "Lead" => "Lead prediction",
                    _ => "QC anomaly",
                };
                ResultsSummaryText.Text =
                    $"{name} · {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {mode}";
            }
            else
            {
                ResultsSummaryText.Text = "";
            }

            AnalysisStatusText.Text = "Complete.";
        }
        finally
        {
            AnalysisProgressBar.Visibility = Visibility.Collapsed;
            RunAnalysisButton.IsEnabled = true;
            ModelListBox.IsEnabled = true;
        }
    }
}
