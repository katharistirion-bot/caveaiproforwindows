# Changelog



All notable changes to **CAVE AI PRO for Windows** are documented here.



Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).



## [Unreleased]



## [1.4.0] - 2026-06-07



### Added

- First-run cinematic intro video after sign-in (skip, mute, replay from Help)

- Publication sheet window — composite plan, elevation, optional 3D overview, legend, and metadata export



### Fixed

- Microsoft Store MSIX now publishes self-contained .NET 8 runtime (`includedFrameworks`) so clean Windows PCs do not prompt for a separate .NET install



## [1.3.0] - 2026-06-07



### Added

- Help → Open diagnostic folder

- Legal terms version tracking (re-accept when EULA document version changes)

- Velopack auto-update bootstrap and InstallationGuard support for Setup.exe installs

- DPAPI encryption for stored Firebase auth token

- Android ↔ Windows sync contract index (`docs/sync-contract.md`)

- Cross-platform survey site type (Cave / Mine / Pothole / Spring) on maps and exports

- Recent files: remove, rename, delete from list



### Fixed

- 3D MODEL tab (sidebar, orbit, clean defaults)

- Velopack update channel and dynamic toolbar version display



### Security

- Release builds no longer honor dev bypass environment variables

- Firebase `listProjectComments` requires project membership

- Collaboration callables re-check premium entitlement server-side



## [1.2.1] - 2026-06-07



### Added

- Cross-platform survey site type on maps and exports

- Recent files: remove, rename, delete from list

- 3D MODEL tab fixes



### Fixed

- Velopack update channel and dynamic toolbar version display



## [1.2.0] - 2026-06-01



### Added

- Windows companion MVP: plan/section, exports, integrity checks, cloud publish hooks



[Unreleased]: https://github.com/katharistirion-bot/caveaiproforwindows/compare/v1.4.0...HEAD

[1.4.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.4.0

[1.3.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.3.0

[1.2.1]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.2.1

[1.2.0]: https://github.com/katharistirion-bot/caveaiproforwindows/releases/tag/v1.2.0

