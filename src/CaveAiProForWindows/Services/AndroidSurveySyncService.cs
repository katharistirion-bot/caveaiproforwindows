using System.IO;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services;

/// <summary>Pair of synchronized Android JSON files used by AI Analytics.</summary>
public sealed class AndroidSurveySyncBundle
{
    public required string SyncFolder { get; init; }

    public string? DatabaseFilePath { get; init; }

    public string? ExportFilePath { get; init; }

    public DateTime? DatabaseLastWriteUtc { get; init; }

    public DateTime? ExportLastWriteUtc { get; init; }

    public required AndroidSurveyAnalyticsContext Context { get; init; }

    public IReadOnlyList<CaveProjectDocument> DatabaseProjects { get; init; } = Array.Empty<CaveProjectDocument>();

    public bool HasAnyFile => DatabaseFilePath != null || ExportFilePath != null;
}

/// <summary>
/// Locates and loads <c>caveai_database_v1.json</c> + <c>android_export.json</c> from Desktop Sync folders.
/// </summary>
public static class AndroidSurveySyncService
{
    public const string DatabaseFileName = "caveai_database_v1.json";
    public const string ExportFileName = "android_export.json";

    public static IReadOnlyList<string> DefaultSyncFolderCandidates()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            Path.Combine(home, "Downloads", "CaveAI"),
            Path.Combine(home, "Documents", "CaveAI"),
            Path.Combine(home, "OneDrive", "CaveAI"),
            Path.Combine(home, "Google Drive", "CaveAI"),
        ];
    }

    public static string? ResolveSyncFolder(string? configuredFolder, string? hintDirectory)
    {
        if (IsSyncFolder(configuredFolder))
            return Path.GetFullPath(configuredFolder!);

        if (IsSyncFolder(hintDirectory))
            return Path.GetFullPath(hintDirectory!);

        foreach (var candidate in DefaultSyncFolderCandidates())
        {
            if (IsSyncFolder(candidate))
                return Path.GetFullPath(candidate);
        }

        if (!string.IsNullOrWhiteSpace(configuredFolder) && Directory.Exists(configuredFolder))
            return Path.GetFullPath(configuredFolder);

        if (!string.IsNullOrWhiteSpace(hintDirectory) && Directory.Exists(hintDirectory))
            return Path.GetFullPath(hintDirectory);

        return null;
    }

    public static bool IsSyncFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        Directory.Exists(folder) &&
        (File.Exists(Path.Combine(folder, DatabaseFileName)) ||
         File.Exists(Path.Combine(folder, ExportFileName)) ||
         Directory.EnumerateFiles(folder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly).Any());

    /// <summary>Newest <c>CaveAI_Backup_*.zip</c> in the sync folder (Android Desktop Sync export target).</summary>
    public static string? TryFindLatestBackupZip(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        return Directory.EnumerateFiles(folder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static AndroidSurveySyncBundle? TryLoadBundle(string syncFolder, string? matchProjectName = null)
    {
        if (string.IsNullOrWhiteSpace(syncFolder) || !Directory.Exists(syncFolder))
            return null;

        syncFolder = Path.GetFullPath(syncFolder);
        var dbPath = Path.Combine(syncFolder, DatabaseFileName);
        var exportPath = Path.Combine(syncFolder, ExportFileName);
        var hasDb = File.Exists(dbPath);
        var hasExport = File.Exists(exportPath);
        if (!hasDb && !hasExport)
            return null;

        var contexts = new List<AndroidSurveyAnalyticsContext>();
        List<CaveProjectDocument> projects = [];

        if (hasDb)
        {
            var json = File.ReadAllText(dbPath);
            projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
            contexts.Add(ImportDatabaseJson(json, DatabaseFileName, matchProjectName, projects));
        }

        if (hasExport)
        {
            var json = File.ReadAllText(exportPath);
            contexts.Add(AndroidSurveyAnalyticsImporter.TryImportStandaloneExport(json, ExportFileName));
        }

        var merged = AndroidSurveyAnalyticsImporter.MergeContexts(
            contexts,
            DescribeSources(hasDb, hasExport, syncFolder));

        return new AndroidSurveySyncBundle
        {
            SyncFolder = syncFolder,
            DatabaseFilePath = hasDb ? dbPath : null,
            ExportFilePath = hasExport ? exportPath : null,
            DatabaseLastWriteUtc = hasDb ? File.GetLastWriteTimeUtc(dbPath) : null,
            ExportLastWriteUtc = hasExport ? File.GetLastWriteTimeUtc(exportPath) : null,
            Context = merged,
            DatabaseProjects = projects,
        };
    }

    /// <summary>Imports a single Android sync JSON file (database, export, or backup data.json).</summary>
    public static AndroidSurveyAnalyticsContext TryImportFromSyncFile(
        string filePath,
        string? matchProjectName = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new AndroidSurveyAnalyticsContext();

        var json = File.ReadAllText(filePath);
        var fileName = Path.GetFileName(filePath);

        if (string.Equals(fileName, ExportFileName, StringComparison.OrdinalIgnoreCase))
            return AndroidSurveyAnalyticsImporter.TryImportStandaloneExport(json, fileName);

        if (string.Equals(fileName, DatabaseFileName, StringComparison.OrdinalIgnoreCase))
        {
            var projects = ExplorationDataLoader.DeserializeProjectsFromText(json);
            return ImportDatabaseJson(json, fileName, matchProjectName, projects);
        }

        return AndroidSurveyAnalyticsImporter.TryImportFromJson(json, fileName);
    }

    private static AndroidSurveyAnalyticsContext ImportDatabaseJson(
        string json,
        string sourceLabel,
        string? matchProjectName,
        IReadOnlyList<CaveProjectDocument> projects)
    {
        if (projects.Count == 0)
            return AndroidSurveyAnalyticsImporter.TryImportFromJson(json, sourceLabel);

        var pick = PickProject(projects, matchProjectName);
        return pick != null
            ? AndroidSurveyAnalyticsImporter.BuildFromProject(pick, sourceLabel)
            : AndroidSurveyAnalyticsImporter.TryImportFromJson(json, sourceLabel);
    }

    private static CaveProjectDocument? PickProject(
        IReadOnlyList<CaveProjectDocument> projects,
        string? matchProjectName)
    {
        if (projects.Count == 0)
            return null;
        if (string.IsNullOrWhiteSpace(matchProjectName))
            return projects[0];

        return projects.FirstOrDefault(p =>
                   string.Equals(p.Name?.Trim(), matchProjectName.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? projects[0];
    }

    private static string DescribeSources(bool hasDb, bool hasExport, string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        if (hasDb && hasExport)
            return $"{name} · {DatabaseFileName} + {ExportFileName}";
        if (hasDb)
            return $"{name} · {DatabaseFileName}";
        return $"{name} · {ExportFileName}";
    }
}

/// <summary>Watches a Desktop Sync folder and raises debounced reload events.</summary>
public sealed class AndroidSurveySyncWatcher : IDisposable
{
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private string? _syncFolder;
    private string? _matchProjectName;

    public event EventHandler<AndroidSurveySyncBundle>? SyncChanged;

    public void Watch(string? syncFolder, string? matchProjectName = null)
    {
        lock (_gate)
        {
            StopWatcherLocked();
            _syncFolder = syncFolder;
            _matchProjectName = matchProjectName;

            if (string.IsNullOrWhiteSpace(syncFolder) || !Directory.Exists(syncFolder))
                return;

            _watcher = new FileSystemWatcher(syncFolder)
            {
                Filter = "*.json",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnWatcherEvent;
            _watcher.Created += OnWatcherEvent;
            _watcher.Renamed += OnWatcherEvent;
        }
    }

    public AndroidSurveySyncBundle? ReloadNow()
    {
        string? folder;
        string? projectName;
        lock (_gate)
        {
            folder = _syncFolder;
            projectName = _matchProjectName;
        }

        return string.IsNullOrWhiteSpace(folder)
            ? null
            : AndroidSurveySyncService.TryLoadBundle(folder, projectName);
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsTrackedFile(e.Name))
            return;

        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new System.Threading.Timer(_ => RaiseSyncChanged(), null, 750, Timeout.Infinite);
        }
    }

    private void RaiseSyncChanged()
    {
        var bundle = ReloadNow();
        if (bundle == null)
            return;
        SyncChanged?.Invoke(this, bundle);
    }

    private static bool IsTrackedFile(string? name) =>
        string.Equals(name, AndroidSurveySyncService.DatabaseFileName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, AndroidSurveySyncService.ExportFileName, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_gate)
            StopWatcherLocked();
    }

    private void StopWatcherLocked()
    {
        _debounce?.Dispose();
        _debounce = null;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatcherEvent;
            _watcher.Created -= OnWatcherEvent;
            _watcher.Renamed -= OnWatcherEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
