using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Persistence;
using Wpf = System.Windows;

namespace CaveAiProForWindows.ViewModels;

/// <summary>Dirty-session UX, discard-confirm, and workspace close/unload coordination.</summary>
public sealed partial class WorkspaceSessionViewModel : ObservableObject
{
    private const string DefaultWindowTitleBase = "CAVE AI PRO — Survey workstation";

    private const string ReadyStatusMessage =
        "Ready — open a CaveAI Pro backup (.json or .zip) for survey QC, Survex/Therion import, Loop closure (Compass/WLS), exports (Survex / Therion / DXF), and batch office workflows. Ctrl+O or drag-and-drop.";

    private readonly MainViewModel _host;
    private string _windowTitleBase = DefaultWindowTitleBase;

    public WorkspaceSessionViewModel(MainViewModel host)
    {
        _host = host;
    }

    /// <summary>Marks the session dirty and shows * in the window title until cleared / successful save.</summary>
    public void MarkDirty(string? statusHint = null)
    {
        if (!_host.IsDirty)
            _host.IsDirty = true;
        RefreshDirtyWindowTitle();
        if (!string.IsNullOrWhiteSpace(statusHint))
            _host.StatusMessage = statusHint;
        _host.NotifySaveProjectCanExecuteChanged();
    }

    public void ClearDirty()
    {
        if (!_host.IsDirty)
            return;
        _host.IsDirty = false;
        RefreshDirtyWindowTitle();
        _host.NotifySaveProjectCanExecuteChanged();
    }

    public void SetWindowTitleBase(string titleWithoutAsterisk)
    {
        _windowTitleBase = string.IsNullOrWhiteSpace(titleWithoutAsterisk)
            ? DefaultWindowTitleBase
            : titleWithoutAsterisk;
        RefreshDirtyWindowTitle();
    }

    private void RefreshDirtyWindowTitle()
    {
        _host.WindowTitle = _host.IsDirty ? _windowTitleBase + " *" : _windowTitleBase;
    }

    /// <summary>True when any backup, catalog snapshot, standalone maps, or ZIP browser state is loaded.</summary>
    public bool HasLoadedWorkspaceContent() => _host.HasLoadedWorkspaceContentInternal();

    /// <summary>CanExecute predicate for <see cref="MainViewModel.CloseWorkspaceCommand"/>.</summary>
    public bool CanCloseWorkspace() => HasLoadedWorkspaceContent();

    /// <summary>
    /// Returns false if the user cancelled. On Yes, attempts save; if save is impossible, warns and returns false.
    /// </summary>
    public bool ConfirmDiscardUnsavedChanges(string? contextLabel = null)
    {
        if (!_host.IsDirty)
            return true;

        var owner = _host.OwnerWindow ?? Wpf.Application.Current.MainWindow;
        var label = string.IsNullOrWhiteSpace(contextLabel) ? "this project" : contextLabel;
        var result = Wpf.MessageBox.Show(
            owner,
            $"You have unsaved changes in {label}.\n\nSave before continuing?",
            "Unsaved changes",
            Wpf.MessageBoxButton.YesNoCancel,
            Wpf.MessageBoxImage.Warning);

        if (result == Wpf.MessageBoxResult.Cancel)
            return false;

        if (result == Wpf.MessageBoxResult.Yes)
        {
            if (!CanSaveProject())
            {
                UserErrorReporter.ShowWarning(
                    owner,
                    "Cannot save this project from here (need a writable .json/.zip source on disk and accepted legal terms). Use Save as / fix the source path, or choose Don't Save.",
                    "Save project");
                return false;
            }

            SaveProject();
            return !_host.IsDirty;
        }

        ClearDirty();
        return true;
    }

    /// <summary>Unloads the open workspace after discard confirmation. Returns false if the user cancelled.</summary>
    public bool TryCloseWorkspace()
    {
        if (!CanCloseWorkspace())
            return false;
        if (!ConfirmDiscardUnsavedChanges("the open workspace"))
            return false;

        _host.ClearWorkspaceDataCore();
        ClearDirty();
        SetWindowTitleBase(DefaultWindowTitleBase);
        _host.StatusMessage = ReadyStatusMessage;
        _host.NotifyWorkspaceCommandStateChanged();
        return true;
    }

    /// <summary>Auto-unload before opening a different backup so the previous session cannot leak into the new project.</summary>
    /// <returns>False if the user cancelled leaving a dirty workspace.</returns>
    public bool UnloadWorkspaceBeforeNewLoad()
    {
        if (!HasLoadedWorkspaceContent())
            return true;
        if (!ConfirmDiscardUnsavedChanges("the current workspace"))
            return false;

        _host.ClearWorkspaceDataCore();
        ClearDirty();
        return true;
    }

    public bool CanSaveProject()
    {
        var primarySourcePath = _host.PrimarySourceFilePath;
        return _host.LegalTermsAccepted &&
               _host.SelectedProject != null &&
               _host.HasSourceOnDisk() &&
               _host.SourceFileCount == 1 &&
               AiRenderSavePathPolicy.CanWriteBesideSourceFile(primarySourcePath) &&
               (Path.GetExtension(primarySourcePath!).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(primarySourcePath!).Equals(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public void SaveProject()
    {
        if (_host.SelectedProject == null || string.IsNullOrEmpty(_host.PrimarySourceFilePath))
            return;

        var primarySourcePath = _host.PrimarySourceFilePath;
        try
        {
            ProjectPersistenceService.Save(new ProjectPersistenceService.SaveRequest
            {
                Projects = _host.Projects.ToList(),
                PrimarySourcePath = primarySourcePath,
                BeforeSerialize = p =>
                {
                    if (ReferenceEquals(p, _host.SelectedProject))
                        _host.PersistProjectBeforeSave?.Invoke(p);
                },
            });

            ClearDirty();
            _host.StatusMessage =
                $"Saved {_host.Projects.Count} project(s) — sketch mapObjects and metadata written to {Path.GetFileName(primarySourcePath)}.";
        }
        catch (Exception ex)
        {
            UserErrorReporter.ShowWarning(
                _host.OwnerWindow ?? Wpf.Application.Current.MainWindow,
                ex.Message,
                "Save project");
        }
    }
}