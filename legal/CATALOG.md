# Legal documents catalog — CAVE AI PRO

This folder collects **legal texts** typically required or recommended for a desktop Windows application, MSI distribution, and use alongside **CaveAI Pro (Android)**.

**Distribution:** Android app via **Google Play**; **CAVE AI PRO** desktop installer **separately**, for **PCs with Microsoft Windows** only — see [00-DISTRIBUTION-PLATFORMS.md](00-DISTRIBUTION-PLATFORMS.md).

> **Important:** These files are **templates**. They must be reviewed by **legal counsel**; fill in publisher details (`[FILL IN]`), governing law, and version before public release or store listing.

## Document index

| # | File | Purpose | When it is typically required |
|---|------|---------|------------------------------|
| 0 | [00-DISTRIBUTION-PLATFORMS.md](00-DISTRIBUTION-PLATFORMS.md) | Google Play (Android) vs Windows MSI; no Play distribution for desktop | Clarifies product and store messaging for users and reviewers |
| 1 | [01-EULA-END-USER-LICENSE.md](01-EULA-END-USER-LICENSE.md) | End User License Agreement (EULA) | Almost always for software distribution; often in installer / About |
| 2 | [02-PRIVACY-POLICY.md](02-PRIVACY-POLICY.md) | Privacy policy / data processing | Mandatory if personal data is collected; **recommended** even for local-only apps (transparency) |
| 3 | [03-TERMS-OF-USE.md](03-TERMS-OF-USE.md) | General terms of software use | Complements the EULA; useful if there is a website, support, or future online services |
| 4 | [04-DISCLAIMER-LIABILITY-SAFETY.md](04-DISCLAIMER-LIABILITY-SAFETY.md) | Liability disclaimer, cave surveying / topography safety | **Strongly recommended** for scientific/professional tools and physical safety |
| 5 | [05-COOKIES-AND-ONLINE-SERVICES.md](05-COOKIES-AND-ONLINE-SERVICES.md) | Cookies / analytics / online (or a “not used” statement) | If there is a website or online features; otherwise a short non-use statement |
| 6 | [06-THIRD-PARTY-NOTICES.md](06-THIRD-PARTY-NOTICES.md) | Third-party licenses (libraries, .NET runtime) | **Mandatory** compliance with open-source / Microsoft redistributable terms |
| 7 | [07-OPEN-SOURCE-LICENSES.md](07-OPEN-SOURCE-LICENSES.md) | Full texts or pointers to OSS licenses | For MIT/BSD etc. when “license copy” is required |
| 8 | [08-GDPR-DATA-CONTROLLER.md](08-GDPR-DATA-CONTROLLER.md) | Data controller, data subject rights (GDPR) | If you serve users in the EU/EEA and process personal data |
| 9 | [09-IMPRESSUM-PUBLISHER.md](09-IMPRESSUM-PUBLISHER.md) | Publisher / contact (Impressum) | Often mandatory in DE/AT; good practice elsewhere |
| 10 | [10-EXPORT-AND-SANCTIONS.md](10-EXPORT-AND-SANCTIONS.md) | Export control / sanctions (if applicable) | If you distribute internationally or in regulated sectors |
| 11 | [11-ACCESSIBILITY-STATEMENT.md](11-ACCESSIBILITY-STATEMENT.md) | Accessibility statement (EN 301 549 / WCAG) | If required by EU public procurement or accessibility policies |
| 12 | [12-TRADEMARKS.md](12-TRADEMARKS.md) | Third-party trademarks (Survex, Windows, etc.) | To avoid confusion; correct disclaimers when naming third parties |
| 14 | [14-CONTACT.md](14-CONTACT.md) | Contact (name, email) | Single reference point for EULA, privacy, GDPR, accessibility |

## Publisher workflow

1. Complete `09-IMPRESSUM-PUBLISHER.md` (publisher identity); contact: `14-CONTACT.md`.
2. Align `01-EULA` and `03-TERMS` with your distribution model (free / paid / trial / enterprise).
3. Update `02-PRIVACY` and `08-GDPR` depending on whether data is sent to servers, analytics, support email, etc.
4. Refresh `06` and `07` whenever dependencies change (`dotnet list package`, self-contained publish).
5. Link these documents from the app **About** dialog and, if needed, from the MSI (EULA acceptance before install).

## Disclaimer

The contents **do not** constitute legal advice. The publisher is responsible for final wording and compliance with applicable law.
