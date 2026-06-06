namespace CaveAiProForWindows.Models;

/// <summary>One Android-exported note, annotation, or lead hint tied to a survey station.</summary>
public sealed class AndroidSurveyStationObservation
{
    public required string StationName { get; init; }

    /// <summary>
    /// SurveyNote · StationAnnotation · LeadPrediction · AiClassification · EventLog · Environment ·
    /// FieldObservation · Water · Airflow · GeologicalMarker · FieldSymbol
    /// </summary>
    public required string Category { get; init; }

    public required string Text { get; init; }

    public string? Source { get; init; }

    public float? Confidence { get; init; }
}

/// <summary>Parsed Android survey context for offline AI analytics (notes, annotations, lead hints).</summary>
public sealed class AndroidSurveyAnalyticsContext
{
    public string? SourceLabel { get; init; }

    public string? ProjectName { get; init; }

    public IReadOnlyList<AndroidSurveyStationObservation> Observations { get; init; } =
        Array.Empty<AndroidSurveyStationObservation>();

    public IReadOnlyDictionary<string, IReadOnlyList<AndroidSurveyStationObservation>> ByStation { get; init; } =
        new Dictionary<string, IReadOnlyList<AndroidSurveyStationObservation>>(StringComparer.OrdinalIgnoreCase);

    public bool HasObservations => Observations.Count > 0;

    public IReadOnlyList<AndroidSurveyStationObservation> ForStation(string station) =>
        ByStation.TryGetValue(station.Trim(), out var list) ? list : Array.Empty<AndroidSurveyStationObservation>();

    public IEnumerable<AndroidSurveyStationObservation> LeadPredictions() =>
        Observations.Where(o =>
            o.Category.Equals("LeadPrediction", StringComparison.OrdinalIgnoreCase) ||
            ContainsLeadKeyword(o.Text) ||
            ContainsLeadKeyword(o.Category));

    public IEnumerable<AndroidSurveyStationObservation> SurveyNotes() =>
        Observations.Where(o =>
            o.Category.Equals("SurveyNote", StringComparison.OrdinalIgnoreCase) ||
            o.Category.Equals("Environment", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<AndroidSurveyStationObservation> StationAnnotations() =>
        Observations.Where(o =>
            o.Category.Equals("StationAnnotation", StringComparison.OrdinalIgnoreCase) ||
            o.Category.Equals("EventLog", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsLeadKeyword(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("lead", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("draft", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("open passage", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("continue", StringComparison.OrdinalIgnoreCase));
}
