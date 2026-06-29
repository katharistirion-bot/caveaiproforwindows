namespace CaveAiProForWindows.Models;

/// <summary>One row for the AI ANALYTICS results <see cref="System.Windows.Controls.DataGrid"/>.</summary>
public sealed class AiAnalyticsMetricRow
{
    public string Station { get; set; } = "";

    /// <summary>Android note, annotation, or lead hint (primary field context).</summary>
    public string AndroidContext { get; set; } = "";

    /// <summary>Local geometry / traverse finding for the same station.</summary>
    public string GeometryContext { get; set; } = "";

    public string Metric { get; set; } = "";

    public string Value { get; set; } = "";

    public AiAnalyticsAlertLevel AlertLevel { get; set; }

    public bool IsPriorityAlert => AlertLevel >= AiAnalyticsAlertLevel.Priority;

    public bool HasAndroidFieldNote => !string.IsNullOrWhiteSpace(AndroidContext);

    /// <summary>True when a single click should highlight this station across Plan / Section / X-Ray.</summary>
    public bool CanSelectStation =>
        !string.IsNullOrWhiteSpace(Station) &&
        !string.Equals(Station.Trim(), "—", StringComparison.Ordinal) &&
        !Metric.StartsWith('—');

    public string AlertLabel => AlertLevel switch
    {
        AiAnalyticsAlertLevel.Critical => "CRITICAL",
        AiAnalyticsAlertLevel.Priority => "PRIORITY",
        AiAnalyticsAlertLevel.Info => "Info",
        _ => "",
    };
}
