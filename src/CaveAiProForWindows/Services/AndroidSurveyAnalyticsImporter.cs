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

            CopyJsonNullableArrayOntoProject(root, project, "fieldCatalogEntries");
            CopyJsonNullableArrayOntoProject(root, project, "rocks");
            CopyJsonArrayOntoProject(root, project, "mapSymbols");
            CopyJsonArrayOntoProject(root, project, "brackets");

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
            case "mapSymbols":
                project.MapSymbols = el.Clone();
                break;
            case "brackets":
                project.Brackets = el.Clone();
                break;
        }
    }

    private static void CopyJsonNullableArrayOntoProject(JsonElement root, CaveProjectDocument project, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
            return;

        switch (propertyName)
        {
            case "fieldCatalogEntries":
                project.FieldCatalogEntries = el.Clone();
                break;
            case "rocks":
                project.Rocks = el.Clone();
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
        ExtractShotSensorObservations(project, observations);
        ExtractEnvironmentSnapshots(project, observations);
        ExtractGeoBioFieldRecords(project, observations, planCoords);
        ExtractBrackets(project, observations, planCoords);
        ExtractMapSymbolObservations(project, observations, planCoords);
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
            if (string.IsNullOrWhiteSpace(snap.StationName))
                continue;

            if (!string.IsNullOrWhiteSpace(snap.Notes))
                AddObservation(list, snap.StationName, "Environment", snap.Notes.Trim(), "stationEnvironmentSnapshots");

            var sensors = FormatEnvironmentSnapshotSensors(snap);
            if (!string.IsNullOrWhiteSpace(sensors))
                AddObservation(list, snap.StationName, "FieldObservation", sensors, "stationEnvironmentSnapshots:sensors");
        }
    }

    private static void ExtractShotSensorObservations(CaveProjectDocument project, List<AndroidSurveyStationObservation> list)
    {
        foreach (var shot in project.Shots)
        {
            if (!shot.IsTraverseLeg)
                continue;

            var from = shot.FromStation.Trim();
            if (string.IsNullOrEmpty(from))
                continue;

            var compact = ShotEnvironment.TryFormatCompactMapLine(shot);
            if (!string.IsNullOrWhiteSpace(compact))
            {
                AddObservation(list, from, "FieldObservation", compact, "shot sensors");
            }

            if (shot.ExtensionData == null)
                continue;

            foreach (var key in ShotFieldObservationExtensionKeys)
            {
                if (!shot.ExtensionData.TryGetValue(key, out var el))
                    continue;
                var text = FormatExtensionObservation(key, el);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var category = ClassifyFieldObservationCategory(key, text);
                AddObservation(list, from, category, text, $"shot extension:{key}");
            }
        }
    }

    private static void ExtractGeoBioFieldRecords(
        CaveProjectDocument project,
        List<AndroidSurveyStationObservation> list,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> planCoords)
    {
        foreach (var rec in GeoBioRecordsService.Build(project))
        {
            var station = rec.Station?.Trim();
            if (string.IsNullOrWhiteSpace(station) && !string.IsNullOrWhiteSpace(rec.CoordinatesSummary))
            {
                if (TryParseCoordsSummary(rec.CoordinatesSummary, out var sx, out var sy) && planCoords.Count > 0)
                    station = AndroidSurveyStationMatcher.FindNearestStation(sx, sy, planCoords);
            }

            if (string.IsNullOrWhiteSpace(station))
                continue;

            var text = BuildGeoBioObservationText(rec);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var category = rec.Category switch
            {
                GeoBioCategory.Organism => "FieldObservation",
                GeoBioCategory.Rock => "GeologicalMarker",
                _ => rec.IsFieldCatalogEntry ? "FieldObservation" : "GeologicalMarker",
            };

            AddObservation(list, station, category, text, rec.SourceLabel);
        }
    }

    private static void ExtractBrackets(
        CaveProjectDocument project,
        List<AndroidSurveyStationObservation> list,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> planCoords)
    {
        if (project.Brackets.ValueKind != JsonValueKind.Array)
            return;

        foreach (var el in project.Brackets.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;

            var station = ReadStationFromObject(el);
            if (string.IsNullOrWhiteSpace(station) &&
                TryReadSurveyXY(el, out var sx, out var sy) &&
                planCoords.Count > 0)
                station = AndroidSurveyStationMatcher.FindNearestStation(sx, sy, planCoords);

            var desc = ReadStringProperty(el, "description", "note", "label", "text");
            var temp = ReadStringProperty(el, "temperature", "tempC", "temp");
            var text = JoinBits(desc, temp);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            AddObservation(list, station ?? "—", "StationAnnotation", text, "brackets");
        }
    }

    private static void ExtractMapSymbolObservations(
        CaveProjectDocument project,
        List<AndroidSurveyStationObservation> list,
        IReadOnlyDictionary<string, SurveyStationGeometry.StationPlanCoords> planCoords)
    {
        foreach (var sym in SurveyStationGeometry.ParsePlanMapSymbols(project))
        {
            var station = AndroidSurveyStationMatcher.FindNearestStation(sym.X, sym.Y, planCoords);
            if (string.IsNullOrWhiteSpace(station))
                continue;

            var label = MapSymbolIconResolver.ExportLabel(sym);
            if (string.IsNullOrWhiteSpace(label))
                label = "Map symbol";

            var kind = AndroidSketchSymbolKindMapper.Resolve(sym.SymbolId, sym.Label, sym.IconKey);
            var category = ClassifyMapSymbolCategory(kind, sym.Label, sym.IconKey, sym.SymbolId, label);
            AddObservation(list, station, category, label.Trim(), "mapSymbols");
        }
    }

    private static readonly string[] ShotFieldObservationExtensionKeys =
    [
        "airflow", "airFlow", "airFlowMps", "draft", "draftDirection", "draftStrength", "wind", "blow",
        "waterLevel", "waterDepthM", "waterDepth", "waterPresent", "water", "poolDepthM", "streamFlow",
    ];

    private static string FormatEnvironmentSnapshotSensors(StationEnvironmentSnapshot snap)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (snap.ManualAmbientTempCelsius is float mt)
            parts.Add($"Temp {mt.ToString("0.#", inv)} °C");
        if (snap.AmbientBleTempCelsius is float bt)
            parts.Add($"Temp (BLE) {bt.ToString("0.#", inv)} °C");
        if (snap.ManualRelativeHumidityPct is float mh)
            parts.Add($"RH {mh.ToString("0.#", inv)} %");
        if (snap.AmbientBleRelativeHumidityPct is float bh)
            parts.Add($"RH (BLE) {bh.ToString("0.#", inv)} %");
        if (snap.Co2Ppm is float co2)
            parts.Add($"CO₂ {co2.ToString("0", inv)} ppm");
        if (snap.BarometricPressureHpa is float bp)
            parts.Add($"{bp.ToString("0", inv)} hPa");
        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    private static string BuildGeoBioObservationText(GeoBioRecord rec)
    {
        var parts = new List<string> { rec.Title };
        if (!string.IsNullOrWhiteSpace(rec.ScientificName) &&
            !rec.ScientificName.Equals(rec.Title, StringComparison.OrdinalIgnoreCase))
            parts.Add(rec.ScientificName);
        if (!string.IsNullOrWhiteSpace(rec.ShortNote))
            parts.Add(rec.ShortNote);
        if (!string.IsNullOrWhiteSpace(rec.BehaviorNotes))
            parts.Add(rec.BehaviorNotes);
        if (!string.IsNullOrWhiteSpace(rec.DetailsSummary))
            parts.Add(rec.DetailsSummary);
        return string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static bool TryParseCoordsSummary(string summary, out float x, out float y)
    {
        x = y = 0;
        var parts = summary.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;
        return float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out x) &&
               float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out y);
    }

    private static string? FormatExtensionObservation(string key, JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString()?.Trim();
        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out var f))
            return $"{HumanizeExtensionKey(key)}: {f.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (el.ValueKind == JsonValueKind.True)
            return HumanizeExtensionKey(key);
        if (el.ValueKind == JsonValueKind.False)
            return null;
        return null;
    }

    private static string ClassifyFieldObservationCategory(string key, string text)
    {
        var k = key.ToLowerInvariant();
        if (k.Contains("water", StringComparison.Ordinal) || k.Contains("pool", StringComparison.Ordinal) ||
            k.Contains("stream", StringComparison.Ordinal))
            return "Water";
        if (k.Contains("air", StringComparison.Ordinal) || k.Contains("draft", StringComparison.Ordinal) ||
            k.Contains("wind", StringComparison.Ordinal) || k.Contains("blow", StringComparison.Ordinal))
            return "Airflow";
        if (ContainsAirflowKeyword(text))
            return "Airflow";
        if (ContainsWaterKeyword(text))
            return "Water";
        return "FieldObservation";
    }

    private static string ClassifyMapSymbolCategory(
        SketchEditorSymbolKind kind,
        string? label,
        string? iconKey,
        string? symbolId,
        string exportLabel)
    {
        var combined = string.Join(' ',
            new[] { label, iconKey, symbolId, exportLabel }.Where(s => !string.IsNullOrWhiteSpace(s)));

        if (kind == SketchEditorSymbolKind.WaterPool || ContainsWaterKeyword(combined))
            return "Water";
        if (ContainsAirflowKeyword(combined) ||
            (symbolId ?? "").Contains("air_draft", StringComparison.OrdinalIgnoreCase) ||
            (iconKey ?? "").Contains("air_draft", StringComparison.OrdinalIgnoreCase))
            return "Airflow";
        if (kind is SketchEditorSymbolKind.RockBlock or SketchEditorSymbolKind.StalactiteSpeleothem
                or SketchEditorSymbolKind.FlowstoneCurtain or SketchEditorSymbolKind.SandMudFloor)
            return "GeologicalMarker";
        return "FieldSymbol";
    }

    private static bool ContainsWaterKeyword(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("water", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("pool", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("stream", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("lake", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("river", StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAirflowKeyword(string? text) =>
        !string.IsNullOrWhiteSpace(text) &&
        (text.Contains("air", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("draft", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("wind", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("blow", StringComparison.OrdinalIgnoreCase));

    private static string HumanizeExtensionKey(string key) =>
        key switch
        {
            "airFlowMps" => "Airflow",
            "draftDirection" => "Draft direction",
            "draftStrength" => "Draft strength",
            "waterLevel" => "Water level",
            "waterDepthM" or "waterDepth" => "Water depth",
            "waterPresent" => "Water present",
            "poolDepthM" => "Pool depth",
            "streamFlow" => "Stream flow",
            _ => char.ToUpperInvariant(key[0]) + key[1..],
        };

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
