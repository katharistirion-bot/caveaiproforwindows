using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>
/// Watches Android Desktop Sync / Downloads folders for new or updated <c>CaveAI_Backup_*.zip</c> files.
/// </summary>
public sealed class AndroidBackupZipWatcher : IDisposable
{
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private string? _watchFolder;

    public event EventHandler<AndroidBackupZipEventArgs>? BackupZipChanged;

    public void Watch(string? folder)
    {
        lock (_gate)
        {
            StopLocked();
            _watchFolder = folder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            _watcher = new FileSystemWatcher(folder)
            {
                Filter = "CaveAI_Backup_*.zip",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };
            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Renamed += OnEvent;
        }
    }

    public string? LatestBackupZip()
    {
        string? folder;
        lock (_gate)
            folder = _watchFolder;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return null;

        return Directory.EnumerateFiles(folder, "CaveAI_Backup_*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Name) ||
            !e.Name.StartsWith("CaveAI_Backup_", StringComparison.OrdinalIgnoreCase) ||
            !e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return;

        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = new System.Threading.Timer(_ => Raise(e.FullPath), null, 1200, Timeout.Infinite);
        }
    }

    private void Raise(string fullPath)
    {
        if (!File.Exists(fullPath))
            return;
        BackupZipChanged?.Invoke(this, new AndroidBackupZipEventArgs(fullPath, File.GetLastWriteTimeUtc(fullPath)));
    }

    public void Dispose()
    {
        lock (_gate)
            StopLocked();
    }

    private void StopLocked()
    {
        _debounce?.Dispose();
        _debounce = null;
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnEvent;
            _watcher.Created -= OnEvent;
            _watcher.Renamed -= OnEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}

public sealed class AndroidBackupZipEventArgs : EventArgs
{
    public AndroidBackupZipEventArgs(string zipPath, DateTime lastWriteUtc)
    {
        ZipPath = zipPath;
        LastWriteUtc = lastWriteUtc;
    }

    public string ZipPath { get; }
    public DateTime LastWriteUtc { get; }
}
