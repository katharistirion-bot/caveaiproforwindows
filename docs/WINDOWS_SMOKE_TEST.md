# Windows smoke test checklist

Run after local `dotnet test` passes and before merging installer/PR changes.

## Startup and shell

- [ ] App launches without XAML parse errors (TabGroupToggle, theme resources)
- [ ] Legal disclaimer gate on first run; LEGAL & SETTINGS tab accepts terms
- [ ] Main window tab groups: Survey / Library / Publish / Settings filter tabs correctly
- [ ] Status chip click opens sign-in or LEGAL & SETTINGS when needed
- [ ] Command palette (`Ctrl+K`): Open project, Surface map, Export, Cloud retry

## Survey workflow (do not break)

- [ ] Open `.json` or `.zip` backup — projects list populates
- [ ] Select project — PLAN tab renders traverse
- [ ] Save project (`Ctrl+S`) writes back without error
- [ ] Export Survex / plan SVG from File or palette

## Maps

- [ ] SURFACE tab: WebView2 loads; hillshade / Copernicus toggles persist
- [ ] Help → Explore map: layer params match Surface tab after close
- [ ] Field Trip Planner: map loading overlay, error message if WebView2 missing
- [ ] Compare backups overlay legend (A solid / B semi-transparent)

## Cloud and settings

- [ ] Preferences: browse sync/export folders; invalid paths show warning
- [ ] Dark theme: TextBox / ComboBox use dark surface colors
- [ ] Cloud publish retry queue (`CloudCommands`) after simulated failure
- [ ] Publish history line in LEGAL & SETTINGS when project selected

## Installer (MSI)

- [ ] Build: `dotnet build installer/CaveAiProForWindows.Installer.wixproj -c Release`
- [ ] Install on clean VM or uninstall/reinstall
- [ ] Finish dialog optional checkbox launches `CaveAiProForWindows.exe`
- [ ] Start menu + desktop shortcuts open app

## Automated baseline

```powershell
cd D:\caveaiproforwindows
dotnet test --no-restore
```

Expect **372+** passing tests (including `SurfaceMapLayerPrefsSyncTests`).
