# Localization (Windows Desktop Companion)

## Policy

The Desktop Companion ships **English-only UI** in v1.5.x. Legal text, menus, dialogs, and status strings are defined in `Services/Localization/AppStrings.cs` and `Services/Legal/LegalTexts.cs`.

## Settings

`AppUiSettingsModel.UiLanguage` is persisted but non-`en` values are reset to English on startup (`UiLocalizationService`).

## Adding a language later

1. Introduce resource files or satellite assemblies per locale.
2. Replace `AppStrings` constants with a lookup layer (keep English as fallback).
3. Extend Store listing / MSI docs with supported locales.
4. Do **not** machine-translate legal text without legal review.

## Greek / other locales

Field workflows on Android may use Greek; the Windows workstation intentionally stays English for publication, QC, and support consistency.