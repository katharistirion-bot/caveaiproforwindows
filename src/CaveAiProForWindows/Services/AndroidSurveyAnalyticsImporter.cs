using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Extracts survey notes, station annotations, and lead hints from Android <c>data.json</c> exports.
/// </summary>
public static class AndroidSurveyAnalyticsImporter
{
    private static readonly JsonSerializerOptions ProjectJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly string[] LeadPredictionArrayKeys =
    [
        "leadPredictions",
        "androidLeadPredictions",
        "surveyLeadPredictions",
        "leadHints",
    ];

    private static readonly string[] StationAnnotationArrayKeys =
    [
        "stationAnnotations",
        "surveyStationAnnotations",
        "stationNotes",
    ];

    public static AndroidSurveyAnalyticsContext TryImportFromJson(string json, string? sourceLabel = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty(sourceLabel);

        try
        {
            var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
            if (projects.Count == 0)
                return Empty(sourceLabel);

            var project = projects[0];
            if (projects.Count > 1)
                sourceLabel = $"{sourceLabel ?? "import"} (+{projects.Count - 1} more projects — first used)";

            return BuildFromProject(project, sourceLabel);
        }
        catch
        {
            return Empty(sourceLabel);
        }
    }

    /// <summary>
    /// Parses standalone <c>android_export.json</c> — project array, single project, or analytics-only object.
    /// </summary>
    public static AndroidSurveyAnalyticsContext TryImportStandaloneExport(string json, string? sourceLabel = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty(sourceLabel);

        var fromProjects = TryImportFromJson(json, sourceLabel);
        if (fromProjects.HasObservations)
            return fromProjects;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Empty(sourceLabel);

            var project = JsonSerializer.Deserialize<CaveProjectDocument>(json, ProjectJsonOptions);
            if (project != null)
            {
                var built = BuildFromProject(project, sourceLabel ?? "android_export.json");
                if (built.HasObservations)
                    return built;
            }

            project = new CaveProjectDocument
            {
                Name = ReadStringProperty(root, "projectName", "name", "caveName") ?? "Android export",
            };

            CopyJsonArrayOntoProject(root, project, "surveyEventLog");
            CopyJsonArrayOntoProject(root, project, "depthSpanAnnotations");
            CopyJsonArrayOntoProject(root, project, "sketches");
            CopyJsonArrayOntoProject(root, project, "sketchLayer");
            CopyJsonArrayOntoProject(root, project, "sectionSketches");
            CopyJsonArrayOntoProject(root, project, "mapObjects");

            project.ExtensionData ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in LeadPredictionArrayKeys
                         .Concat(StationAnnotationArrayKeys)
                         .Append("surveyNotes"))
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    project.ExtensionData[key] = arr.Clone();
            }

            return BuildFromProject(project, sourceLabel ?? "android_export.json");
        }
        catch
        {
            return Empty(sourceLabel);
        }
    }

    /// <summary>Merges observations from database + export contexts (deduped by station/category/text).</summary>
    public static AndroidSurveyAnalyticsContext MergeContexts(
        IEnumerable<AndroidSurveyAnalyticsContext> contexts,
        string sourceLabel)
    {
        var observations = new List<AndroidSurveyStationObservation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? projectName = null;

        foreach (var ctx in contexts)
        {
            if (ctx == null)
                continue;
            projectName ??= ctx.ProjectName;
            foreach (var obs in ctx.Observations)
            {
                var key = $"{obs.StationName}|{obs.Category}|{obs.Text}";
                if (!seen.Add(key))
                    continue;
                observations.Add(obs);
            }
        }

        var byStation = observations
            .GroupBy(o => o.StationName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AndroidSurveyStationObservation>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new AndroidSurveyAnalyticsContext
        {
            SourceLabel = sourceLabel,
            ProjectName = projectName,
            Observations = observations,
            ByStation = byStation,
        };
    }

    private static void CopyJsonArrayOntoProject(JsonElement root, CaveProjectDocument project, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
            return;

        switch (propertyName)
        {
            case "surveyEventLog":
                project.SurveyEventLog = el.Clone();
                break;
            case "depthSpanAnnotations":
                project.DepthSpanAnnotations = el.Clone();
                break;
            case "sketches":
                project.Sketches = el.Clone();
                break;
            case "sketchLayer":
                project.SketchLayer = el.Clone();
                break;
            case "sectionSketches":
                project.SectionSketches = el.Clone();
                break;
            case "mapObjects":
                project.MapObjects = el.Clone();
                break;
        }
    }

    public static AndroidSurveyAnalyticsContext BuildFromProject(
        CaveProjectDocument project,
        string? sourceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var observations = new List<AndroidSurveyStationObservation>();
        var stationNames = CollectStationNames(project);
        var planCoords = SurveyStationGeometry.CalculatePlanCoordinates(project);

        ExtractShotSurveyNotes(project, observations);
        ExtractEnvironmentSnapshots(project, observations);
        ExtractAiClassifications(project, observations);
        ExtractSurveyEventLog(project, observations);
        ExtractDepthSpanAnnotations(project, observations, stationNames);
        ExtractSketchTextAnnotations(project, observations, planCoords);
        ExtractExtensionArrays(project, observations);

        var byStation = observations
            .GroupBy(o => o.StationName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AndroidSurveyStationObservation>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new AndroidSurveyAnalyticsContext
        {
            SourceLabel = sourceLabel ?? project.LoadedFromFile ?? "loaded project",
            ProjectName = project.Name,
            Observations = observations,
            ByStation = byStation,
        };
    }

    private static AndroidSurveyAnalyticsContext Empty(string? sourceLabel) => new()
    {
        SourceLabel = sourceLabel,
    };

    private static HashSet<string> CollectStationNames(CaveProjectDocument project)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in project.Shots)
        {
            if (!string.IsNullOrWhiteSpace(s.FromStation))
                set.Add(s.FromStation.Trim());
            if (!string.IsNullOrWhiteSpace(s.ToStation) && s.ToStation != "-")
                set.Add(s.ToStation.Trim());
        }

        foreach (var snap in project.StationEnvironmentSnapshots)
        {
            if (!string.IsNullOrWhiteSpace(snap.StationName))
                set.Add(snap.StationName.Trim());
        }

        return set;
    }

    private static void ExtractShotSurveyNotes(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        foreach (var shot in project.Shots)
        {
            var from = (shot.FromStation ?? "").Trim();
            var to = (shot.ToStation ?? "").Trim();
            var leg = string.IsNullOrEmpty(to) || to == "-" ? from : $"{from} → {to}";

            if (!string.IsNullOrWhiteSpace(shot.Notes))
            {
                AddObservation(list, from, "SurveyNote", shot.Notes.Trim(), $"shot notes ({leg})");
                if (!string.IsNullOrEmpty(to) && to != "-" && !to.Equals(from, StringComparison.OrdinalIgnoreCase))
                    AddObservation(list, to, "SurveyNote", shot.Notes.Trim(), $"shot notes ({leg})");
            }

            if (!string.IsNullOrWhiteSpace(shot.Comment))
            {
                AddObservation(list, from, "SurveyNote", shot.Comment.Trim(), $"shot comment ({leg})");
                if (!string.IsNullOrEmpty(to) && to != "-" && !to.Equals(from, StringComparison.OrdinalIgnoreCase))
                    AddObservation(list, to, "SurveyNote", shot.Comment.Trim(), $"shot comment ({leg})");
            }
        }
    }

    private static void ExtractEnvironmentSnapshots(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        foreach (var snap in project.StationEnvironmentSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snap.Notes) || string.IsNullOrWhiteSpace(snap.StationName))
                continue;
            AddObservation(list, snap.StationName, "Environment", snap.Notes.Trim(), "stationEnvironmentSnapshots");
        }
    }

    private static void ExtractAiClassifications(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        foreach (var tag in project.SurveyAiClassifications)
        {
            var label = !string.IsNullOrWhiteSpace(tag.LabelLocale) ? tag.LabelLocale : tag.Label;
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var station = ResolveEntityStation(tag.EntityType, tag.EntityRef, project);
            if (string.IsNullOrWhiteSpace(station))
                continue;

            var category = IsLeadClassification(tag, label) ? "LeadPrediction" : "AiClassification";
            var text = tag.Confidence is { } c
                ? $"{label.Trim()} (p={c:0.##})"
                : label.Trim();
            AddObservation(list, station, category, text, "surveyAiClassifications", tag.Confidence);
        }
    }

    private static void ExtractSurveyEventLog(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        if (project.SurveyEventLog.ValueKind != JsonValueKind.Array)
            return;

        foreach (var el in project.SurveyEventLog.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                    AddObservation(list, "—", "EventLog", el.GetString()!.Trim(), "surveyEventLog");
                continue;
            }

            var station = ReadStationFromObject(el);
            var text = ReadTextFromObject(el);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var type = ReadStringProperty(el, "type", "eventType", "category") ?? "event";
            var category = type.Contains("lead", StringComparison.OrdinalIgnoreCase)
                ? "LeadPrediction"
                : "EventLog";
            AddObservation(list, station ?? "—", category, text, $"surveyEventLog:{type}");
        }
    }

    private static void ExtractDepthSpanAnnotations(
        CaveProjectDocument project,
        List<AndroidSurveyStationObservation> list,
        HashSet<string> stationNames)
    {
        if (project.DepthSpanAnnotations.ValueKind != JsonValueKind.Array)
            return;

        foreach (var el in project.DepthSpanAnnotations.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            var station = ReadStationFromObject(el);
            var text = ReadTextFromObject(el);
            if (string.IsNullOrWhiteSpace(text))
            {
                var from = ReadStringProperty(el, "fromStation", "from");
                var to = ReadStringProperty(el, "toStation", "to");
                var span = ReadStringProperty(el, "label", "depthSpanM", "spanM");
                if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
                    text = JoinBits(from, to, span);
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (string.IsNullOrWhiteSpace(station))
                station = ReadStringProperty(el, "fromStation", "from") ?? "—";

            AddObservation(list, station, "StationAnnotation", text, "depthSpanAnnotations");
        }
    }

    private static void ExtractSketchTextAnnotations(
        CaveProjectDocument project,
        List<AndroidSurveyStationObservation> list,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> planCoords)
    {
        ExtractTextAnnotationsFromElement(project.Sketches, list, planCoords, "sketches");
        ExtractTextAnnotationsFromElement(project.SketchLayer, list, planCoords, "sketchLayer");
        ExtractTextAnnotationsFromElement(project.SectionSketches, list, planCoords, "sectionSketches");
        ExtractTextAnnotationsFromElement(project.MapObjects, list, planCoords, "mapObjects");
    }

    private static void ExtractTextAnnotationsFromElement(
        JsonElement root,
        List<AndroidSurveyStationObservation> list,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> planCoords,
        string source)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return;

        foreach (var stroke in root.EnumerateArray())
        {
            if (stroke.ValueKind != JsonValueKind.Object)
                continue;
            if (!stroke.TryGetProperty("textAnnotations", out var annArr) || annArr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var ann in annArr.EnumerateArray())
            {
                if (ann.ValueKind != JsonValueKind.Object)
                    continue;
                var text = ReadStringProperty(ann, "text", "label", "note");
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var station = ReadStationFromObject(ann);
                if (string.IsNullOrWhiteSpace(station) &&
                    TryReadSurveyXY(ann, out var sx, out var sy) &&
                    planCoords.Count > 0)
                    station = AndroidSurveyStationMatcher.FindNearestStation(sx, sy, planCoords) ?? "—";

                AddObservation(list, station ?? "—", "StationAnnotation", text.Trim(), $"{source}:textAnnotations");
            }
        }
    }

    private static void ExtractExtensionArrays(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        if (project.ExtensionData == null)
            return;

        foreach (var key in LeadPredictionArrayKeys)
        {
            if (!project.ExtensionData.TryGetValue(key, out var el))
                continue;
            ExtractObservationArray(el, list, "LeadPrediction", key);
        }

        foreach (var key in StationAnnotationArrayKeys)
        {
            if (!project.ExtensionData.TryGetValue(key, out var el))
                continue;
            ExtractObservationArray(el, list, "StationAnnotation", key);
        }

        if (project.ExtensionData.TryGetValue("surveyNotes", out var notesEl))
            ExtractObservationArray(notesEl, list, "SurveyNote", "surveyNotes");
    }

    private static void ExtractObservationArray(
        JsonElement el,
        List<AndroidSurveyStationObservation> list,
        string defaultCategory,
        string sourceKey)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    AddObservation(list, "—", defaultCategory, s.Trim(), sourceKey);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var station = ReadStationFromObject(item) ?? "—";
            var text = ReadTextFromObject(item);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var category = ReadStringProperty(item, "category", "type") ?? defaultCategory;
            if (category.Contains("lead", StringComparison.OrdinalIgnoreCase))
                category = "LeadPrediction";

            float? conf = null;
            if (TryReadFloat(item, out var c, "confidence", "score", "probability"))
                conf = c;

            AddObservation(list, station, category, text, sourceKey, conf);
        }
    }

    private static string? ResolveEntityStation(string? entityType, string? entityRef, CaveProjectDocument project)
    {
        if (string.IsNullOrWhiteSpace(entityRef))
            return null;

        var type = (entityType ?? "").Trim().ToLowerInvariant();
        var r = entityRef.Trim();

        if (type is "station" or "stn")
            return r;

        if (type is "shot" or "leg")
        {
            foreach (var shot in project.Shots)
            {
                if (string.Equals(shot.Id, r, StringComparison.OrdinalIgnoreCase))
                    return shot.FromStation.Trim();
            }

            return null;
        }

        if (project.Shots.Any(s =>
                string.Equals(s.FromStation, r, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.ToStation, r, StringComparison.OrdinalIgnoreCase)))
            return r;

        return r;
    }

    private static bool IsLeadClassification(SurveyAiClassificationTag tag, string label) =>
        (tag.EntityType ?? "").Contains("lead", StringComparison.OrdinalIgnoreCase) ||
        (tag.Label ?? "").Contains("lead", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("lead", StringComparison.OrdinalIgnoreCase) ||
        label.Contains("draft", StringComparison.OrdinalIgnoreCase);

    private static void AddObservation(
        List<AndroidSurveyStationObservation> list,
        string station,
        string category,
        string text,
        string source,
        float? confidence = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        station = string.IsNullOrWhiteSpace(station) ? "—" : station.Trim();
        list.Add(new AndroidSurveyStationObservation
        {
            StationName = station,
            Category = category,
            Text = text,
            Source = source,
            Confidence = confidence,
        });
    }

    private static string? ReadStationFromObject(JsonElement o) =>
        ReadStringProperty(o, "station", "stationName", "fromStation", "atStation", "stn", "name");

    private static string? ReadTextFromObject(JsonElement o) =>
        ReadStringProperty(o, "text", "message", "note", "notes", "body", "description", "comment", "label", "prediction");

    private static string? ReadStringProperty(JsonElement o, params string[] names)
    {
        foreach (var name in names)
        {
            if (!o.TryGetProperty(name, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
            else if (el.ValueKind == JsonValueKind.Number)
                return el.GetRawText();
        }

        return null;
    }

    private static bool TryReadSurveyXY(JsonElement o, out float x, out float y)
    {
        x = y = 0;
        if (TryReadFloat(o, out x, "surveyX", "x", "east", "e") &&
            TryReadFloat(o, out y, "surveyY", "y", "north", "n"))
            return true;
        return false;
    }

    private static bool TryReadFloat(JsonElement o, out float value, params string[] names)
    {
        value = 0;
        foreach (var name in names)
        {
            if (!o.TryGetProperty(name, out var el))
                continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out var f))
            {
                value = f;
                return true;
            }

            if (el.ValueKind == JsonValueKind.String &&
                float.TryParse(el.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static string JoinBits(params string?[] parts)
    {
        var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToList();
        return list.Count == 0 ? "" : string.Join(" · ", list);
    }
}
