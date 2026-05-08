namespace CaveAiProForWindows.Models;

/// <summary>One row for the AI ANALYTICS results <see cref="System.Windows.Controls.DataGrid"/>.</summary>
public sealed class AiAnalyticsMetricRow
{
    public string Metric { get; set; } = "";

    public string Value { get; set; } = "";
}
