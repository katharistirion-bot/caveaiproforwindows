using System.IO;
using System.Text;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.Persistence;

namespace CaveAiProForWindows.Services;

/// <summary>CPU- and IO-heavy parts of opening backups (ZIP/JSON), run off the UI thread.</summary>
public static class LoadFromPathsWorker
{
    /// <summary>Slots from Android <c>map_inventory.json</c> that are traverse media, not cartography.</summary>
    private static bool IsTraverseShotMapInventorySlot(string? slot) =>
        !string.IsNullOrEmpty(slot) &&
        slot.StartsWith("traverse.shot", StringComparison.OrdinalIgnoreCase);

    public static LoadFromPathsWorkResult Execute(
        IReadOnlyList<string> paths,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var merged = new List<CaveProjectDocument>();
        var libraryAccumulator = new List<KnownCaveRecord>();
        var loadedPaths = new List<string>();
        var analyticsSb = new StringBuilder();
        var mapInventoryRows = new List<MapInventoryRow>();
        var jsonSurveyLoadFailures = new List<JsonSurveyLoadFailure>();

        var distinct = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var total = distinct.Count;
        var index = 0;

        foreach (var path in distinct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".json" && ext != ".zip")
                continue;
            var fn = Path.GetFileName(path);
            progress?.Report($"Reading {fn} ({index}/{total})…");
            var fullPath = Path.GetFullPath(path);
            var length = new FileInfo(fullPath).Length;
            if (length > BackupFileSizeLimits.MaxBackupFileBytes)
            {
                throw new InvalidDataException(
                    $"{fn} is too large ({length:N0} bytes). For stability, open files up to {BackupFileSizeLimits.MaxBackupFileBytes:N0} bytes ({BackupFileSizeLimits.MaxBackupFileBytes / (1024 * 1024)} MiB) only.");
            }

            var part = new List<CaveProjectDocument>();
            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                var (projects, rawJson) = ExplorationDataLoader.LoadFromCaveAiBackupZipWithRaw(path);
                part = projects;
                analyticsSb.AppendLine(BackupDataJsonAnalytics.BuildReport(fn, rawJson));
                analyticsSb.AppendLine();
                foreach (var row in ZipMapInventoryReader.TryRead(path))
                {
                    if (IsTraverseShotMapInventorySlot(row.Slot))
                        continue;
                    mapInventoryRows.Add(row);
                }

                libraryAccumulator.AddRange(CaveLibraryJsonLoader.TryLoadFromZip(fullPath));
            }
            else
            {
                var rawJson = File.ReadAllText(path, Encoding.UTF8);
                Exception? deserializeEx = null;
                try
                {
                    part = ExplorationDataLoader.DeserializeProjectsFromText(rawJson);
                }
                catch (Exception ex)
                {
                    deserializeEx = ex;
                    part = new List<CaveProjectDocument>();
                }

                if (part.Count > 0)
                {
                    analyticsSb.AppendLine(BackupDataJsonAnalytics.BuildReport(fn, rawJson));
                    analyticsSb.AppendLine();
                }
                else
                {
                    var caves = CaveLibraryJsonLoader.TryDeserializeKnownCaves(rawJson, fullPath);
                    libraryAccumulator.AddRange(caves);
                    if (caves.Count > 0)
                        analyticsSb.AppendLine($"// {fn}: Cave Library ({caves.Count} card(s)) — not a survey database.");
                    else if (deserializeEx != null)
                        jsonSurveyLoadFailures.Add(new JsonSurveyLoadFailure(fullPath, fn, deserializeEx.Message));
                }
            }

            foreach (var p in part)
            {
                p.LoadedFromFile = fullPath;
                GenerativeAssetPersistenceService.TryHydrateSessionFromProject(p, fullPath);
            }
            merged.AddRange(part);
            loadedPaths.Add(path);
            RecentPathsStore.Push(path);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var orderedPaths = loadedPaths;
        string? zipPath = orderedPaths.Count == 1 &&
                          string.Equals(Path.GetExtension(orderedPaths[0]), ".zip", StringComparison.OrdinalIgnoreCase)
            ? orderedPaths[0]
            : null;
        var auxiliaryZip = orderedPaths
            .FirstOrDefault(p =>
                string.Equals(Path.GetExtension(p), ".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p));

        IntegrityReport? integrityReport = null;
        if (zipPath != null)
        {
            progress?.Report("Verifying backup integrity (SHA-256)…");
            integrityReport = IntegrityVerifier.VerifyZip(zipPath);
        }

        return new LoadFromPathsWorkResult
        {
            Merged = merged,
            LibraryRecords = libraryAccumulator,
            LoadedPaths = orderedPaths,
            BackupDataAnalyticsText = analyticsSb.ToString().Trim(),
            MapInventoryRows = mapInventoryRows,
            IntegrityZipPath = zipPath,
            AuxiliaryZipForMaps = auxiliaryZip,
            IntegrityReport = integrityReport,
            JsonSurveyLoadFailures = jsonSurveyLoadFailures,
        };
    }
}
