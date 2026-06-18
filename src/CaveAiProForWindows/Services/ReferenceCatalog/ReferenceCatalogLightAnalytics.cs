using System.IO;
using System.Text.Json;

namespace CaveAiProForWindows.Services.ReferenceCatalog;

/// <summary>Local-only usage counters (debug log + optional JSON in diagnostic folder).</summary>
public static class ReferenceCatalogLightAnalytics
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, int> Counters = new(StringComparer.OrdinalIgnoreCase);

    public static void Increment(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        lock (Gate)
        {
            Counters.TryGetValue(eventName, out var n);
            Counters[eventName] = n + 1;
        }

        App.WriteStartupLog($"[analytics] {eventName} (+1)");
    }

    public static IReadOnlyDictionary<string, int> Snapshot()
    {
        lock (Gate)
        {
            return new Dictionary<string, int>(Counters, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string AnalyticsJsonPath =>
        Path.Combine(DiagnosticLogPaths.AppDataDirectory, "light-analytics.json");

    public static void PersistSnapshot()
    {
        try
        {
            Directory.CreateDirectory(DiagnosticLogPaths.AppDataDirectory);
            var payload = new AnalyticsFile
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Events = Snapshot().ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            File.WriteAllText(AnalyticsJsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* best effort */
        }
    }

    private sealed class AnalyticsFile
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public Dictionary<string, int> Events { get; set; } = new();
    }

    public static class Events
    {
        public const string CatalogOpen = "catalog_open";
        public const string CatalogForceRefresh = "catalog_force_refresh";
        public const string CatalogShareLinkCopy = "catalog_share_link_copy";
        public const string CatalogSurveyStartLink = "catalog_survey_start_link";
        public const string FieldTripExport = "field_trip_export";
        public const string FieldTripExportPdf = "field_trip_export_pdf";
        public const string ReferenceCompareOpen = "reference_compare_open";
        public const string PublishReferenceMatchShown = "publish_reference_match_shown";
        public const string XRayReferencePinsShown = "xray_reference_pins_shown";
        public const string PlanReferencePinsShown = "plan_reference_pins_shown";
        public const string SurveyReferenceCompare = "survey_reference_compare";
    }
}
