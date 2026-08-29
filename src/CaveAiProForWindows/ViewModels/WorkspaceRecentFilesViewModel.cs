using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Views;
using Wpf = System.Windows;

namespace CaveAiProForWindows.ViewModels;

public sealed partial class WorkspaceRecentFilesViewModel : ObservableObject
{
    private readonly MainViewModel _host;

    public WorkspaceRecentFilesViewModel(MainViewModel host)
    {
        _host = host;
        RefreshRecentUi();
    }

    public ObservableCollection<string> RecentPaths { get; } = new();

    public void OpenRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (RecentPathFileOps.IsDirectory(path))
        {
            SnackbarService.RevealInExplorer(path);
            return;
        }

        if (!File.Exists(path))
            return;
        _host.LoadFromPathPublic(path);
    }

    public void RemoveRecent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        RecentPathsStore.Remove(path);
        RefreshRecentUi();
    }

    public void RevealRecentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !RecentPathFileOps.Exists(path))
            return;
        SnackbarService.RevealInExplorer(path);
    }

    public void RenameRecentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!RecentPathFileOps.Exists(path))
        {
            Wpf.MessageBox.Show(
                _host.OwnerWindow,
                "This path no longer exists — removing it from the list.",
                "Recent files",
                Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Information);
            RecentPathsStore.Remove(path);
            RefreshRecentUi();
            return;
        }

        if (!RecentPathRenameWindow.TryPrompt(_host.OwnerWindow, path, out var newPath) ||
            string.IsNullOrWhiteSpace(newPath))
            return;

        RecentPathsStore.ReplacePath(path, newPath);
        _host.ApplyPrimarySourcePathRename(path, newPath);
        RefreshRecentUi();
        SnackbarService.Show($"Renamed to {Path.GetFileName(newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}");
    }

    public void DeleteRecentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!RecentPathFileOps.Exists(path))
        {
            RecentPathsStore.Remove(path);
            RefreshRecentUi();
            return;
        }

        var isDir = RecentPathFileOps.IsDirectory(path);
        var message = isDir
            ? $"Delete this folder and everything inside it?\n\n{path}"
            : $"Delete this file permanently?\n\n{path}";
        var owner = _host.OwnerWindow;
        if (Wpf.MessageBox.Show(owner, message, isDir ? "Delete folder" : "Delete file",
                Wpf.MessageBoxButton.YesNo, Wpf.MessageBoxImage.Warning) != Wpf.MessageBoxResult.Yes)
            return;

        if (!RecentPathFileOps.TryDelete(path, out var error))
        {
            Wpf.MessageBox.Show(owner, error ?? "Delete failed.", "Delete", Wpf.MessageBoxButton.OK,
                Wpf.MessageBoxImage.Error);
            return;
        }

        RecentPathsStore.Remove(path);
        _host.CloseWorkspaceIfPrimary(path);
        RefreshRecentUi();
        SnackbarService.Show(isDir ? "Folder deleted." : "File deleted.");
    }

    public void RefreshRecentUi()
    {
        RecentPaths.Clear();
        foreach (var p in RecentPathsStore.Load())
            RecentPaths.Add(p);
    }
}