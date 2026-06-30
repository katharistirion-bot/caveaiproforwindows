# Architecture notes (Windows)

## MainViewModel split (deferred L)

MainViewModel remains a single orchestrator for v1.5.x. Planned v2 split: WorkspaceViewModel, ExportCommandsViewModel, CloudCommandsViewModel.

## Shell layout

ShellLayoutStore complements WindowPlacementStore (column widths, survey tab, last project).

## Install channels

Velopack: desktop + Start Menu shortcuts, GitHub delta updates. WiX MSI: same shortcuts, OpenWith for json/zip, caveaipro:// protocol. MSIX: Store updates, manifest FTA + protocol (prep).
