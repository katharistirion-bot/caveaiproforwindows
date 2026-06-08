namespace CaveAiProForWindows.Services.Legal;

/// <summary>
/// Legal copy for CAVE AI PRO (Windows). Keep aligned with Android <c>LegalTexts.kt</c> and
/// website <c>src/content/legalTexts.js</c> — adapted for the free desktop companion.
/// </summary>
public static class LegalTexts
{
    public const string PublisherName = "Georgios Kourentzis";
    public const string ContactEmail = "caveaipro@gmail.com";
    public const string DocumentVersion = "1.2";
    public const string LastUpdated = "6 June 2026";

    public const string CopyrightNotice =
        "Copyright © 2026 Georgios Kourentzis. All Rights Reserved.";

    public const string TrademarkNotice =
        "CAVE AI PRO™, CaveAI Pro™, the CaveAI Pro name and logo, and related branding are trademarks of Georgios Kourentzis. " +
        "Unauthorized use, imitation, or registration of confusingly similar names or logos is prohibited. " +
        "Third-party names (Microsoft®, Windows®, Survex®, Google Play®, Firebase®, Replicate®, etc.) belong to their respective owners.";

    public const string PublisherImpressum =
        PublisherName + " (individual developer). Contact: " + ContactEmail + ". " +
        "Postal address for formal legal and GDPR correspondence is available upon request at the same email address.";

    /// <summary>Full EULA + safety disclaimer shown in LEGAL &amp; SETTINGS.</summary>
    public const string FullDisclaimerAndEula = """
        LEGAL DISCLAIMER & END USER LICENSE AGREEMENT (EULA) — CAVE AI PRO FOR WINDOWS
        © 2026 Georgios Kourentzis. All Rights Reserved.

        Document version: 1.2 · Last updated: 6 June 2026
        Publisher / Licensor / Developer: Georgios Kourentzis · caveaipro@gmail.com

        IMPORTANT: This document is a legal agreement. If you do not agree, do not install or use the Software.

        WARNING: CAVE EXPLORATION AND SUBTERRANEAN SURVEYING ARE INHERENTLY DANGEROUS ACTIVITIES.
        By installing or using "CAVE AI PRO" for Windows (the "Software"), you acknowledge, understand, and explicitly agree to the following terms, to the maximum extent permitted by applicable mandatory law (including EU/EEA consumer protection where it applies).

        1. DEFINITIONS: "Developer" means Georgios Kourentzis. "You" means the individual or entity using the Software. "AI Output" means any result produced by artificial intelligence or machine-learning features inside or invoked by the Software (including optional cloud-backed Replicate ControlNet map rendering when you are signed in with an entitled CaveAI Pro account).

        2. ELIGIBILITY: You represent that you are legally competent to enter into this agreement and, if you use the Software in hazardous environments, that you are appropriately trained or under qualified supervision. The Software is not intended for children under 16.

        3. ASSUMPTION OF RISK: Speleology and underground work involve extreme risks including serious injury, entrapment, flooding, hypothermia, rockfall, gas hazards, disorientation, equipment failure, or death. You voluntarily assume all risks arising from your activities and from reliance on any information produced by the Software.

        4. NOT LIFE-SAFETY OR EMERGENCY EQUIPMENT: The Software is a supplementary tool for surveying, documentation, and office workflows. It is NOT a substitute for certified rescue equipment, professional rescue services, or emergency response.

        5. NOT SOLE NAVIGATION / REDUNDANCY: You MUST NOT rely on the Software as your sole or primary means of navigation underground. Maintain independent, redundant means (physical compass, disto/paper notes, survey-grade procedures, and team protocols).

        6. NO WARRANTY OF ACCURACY OR FITNESS: Outputs depend on imported data quality and algorithm assumptions. TO THE MAXIMUM EXTENT PERMITTED BY LAW, THE DEVELOPER DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, INCLUDING MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, ACCURACY, RELIABILITY, COMPLETENESS, NON-INFRINGEMENT, AND UNINTERRUPTED OR ERROR-FREE OPERATION. ANY RELIANCE ON COORDINATES, MAPS, EXPORTS (Survex, Therion, DXF, CSV), QC MESSAGES, OR AI OUTPUT IS AT YOUR SOLE RISK.

        7. NO PROFESSIONAL ADVICE: AI Output and on-screen information are not professional engineering, legal, biological, geological, or safety advice. Catalog and field entries are indicative only — not scientific identification or diagnosis.

        8. LIMITATION OF LIABILITY: TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, IN NO EVENT SHALL GEORGIOS KOURENTZIS OR HIS LICENSORS BE LIABLE FOR ANY INDIRECT, INCIDENTAL, SPECIAL, CONSEQUENTIAL, EXEMPLARY, OR PUNITIVE DAMAGES, OR FOR LOSS OF PROFITS, DATA, GOODWILL, OR LIFE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGES. WHERE LIABILITY CANNOT BE EXCLUDED, IT SHALL BE LIMITED TO FIFTY US DOLLARS (USD 50) FOR THE FREE DESKTOP EDITION, EXCEPT WHERE MANDATORY LAW REQUIRES OTHERWISE.

        9. INDEMNIFICATION: You agree to indemnify, defend, and hold harmless Georgios Kourentzis from any claims, damages, losses, liabilities, and expenses (including reasonable legal fees) arising from your use of the Software, your field activities, your violation of this EULA, or your violation of third-party rights, to the extent permitted by law.

        10. INTELLECTUAL PROPERTY & OWNERSHIP: All source code, object code, UI, graphics, logos, documentation, survey algorithms, and compilations in the Software are owned by Georgios Kourentzis and protected by copyright, trade secret, and trademark laws. This EULA grants you a personal, non-exclusive, non-transferable, revocable licence to use the Software in object-code form only. No ownership or intellectual property rights are transferred to you. You may not remove or alter copyright, trademark, or attribution notices.

        11. TRADEMARKS: CAVE AI PRO and CaveAI Pro are used to identify software published by Georgios Kourentzis. You may not use these marks to imply endorsement, affiliation, or a separate product without prior written permission.

        12. PROHIBITED CONDUCT: You may not reverse engineer, decompile, or disassemble the Software except where mandatory law allows; circumvent technical measures; remove legal notices; use the Software to violate law; redistribute the Software outside authorised channels; or use the Developer’s name or marks in a misleading way.

        13. DESKTOP COMPANION (FREE): The Windows edition is distributed as a free companion to CaveAI Pro (Android). It is not sold through Google Play. Optional network features (Public Library WebView, Push to Cloud, update checks, optional cloud generative AI map rendering) operate only when you initiate them and where an active subscription or trial applies.

        13A. CLOUD GENERATIVE AI: Optional AI map rendering (Sketch Editor → AI Render) sends your structure mask and prompt to CaveAI cloud services via Firebase. You must sign in with the same Google account as CaveAI Pro on Android and hold an active subscription or trial. Survey files are not uploaded as part of AI rendering unless you separately choose Push to Cloud or Public Library.

        13B. PUSH TO CLOUD / PUBLIC LIBRARY: If you publish survey data or images to Firebase or the Public Library, you are solely responsible for lawfulness, accuracy, rights, permissions, and safety of published content. Do not publish confidential or safety-sensitive information without authority and consent.

        13C. LOCAL DATA & DIAGNOSTICS: Survey files you open remain on your PC unless you export or publish them. Error logs may be stored locally under %LOCALAPPDATA%\CaveAiProForWindows\ for troubleshooting only.

        14. THIRD-PARTY SERVICES: The Software may interoperate with Microsoft Windows, WebView2, Firebase, Google sign-in (via embedded web), GitHub (updates), Replicate (cloud generative AI backend), and map or export formats under their respective terms.

        15. TERMINATION: This licence terminates automatically if you breach these terms. Upon termination you must stop using and delete all copies. Surviving sections include disclaimers, liability limits, indemnity, IP, and governing law.

        16. EXPORT & SANCTIONS: You represent that you are not prohibited from receiving the Software under applicable export or sanctions laws.

        17. GOVERNING LAW & JURISDICTION: These terms are governed by the laws of Greece. Subject to mandatory consumer protections, exclusive jurisdiction lies with the competent courts of Athens, Greece. EU consumers retain mandatory rights in their country of residence.

        18. ENTIRE AGREEMENT: This EULA constitutes the entire agreement for the Windows Software regarding legal terms. The Android app may have additional store terms when obtained from Google Play.

        By checking “I accept” and using CAVE AI PRO for Windows, you confirm that you have read this EULA, that you accept it, and that Georgios Kourentzis remains the exclusive owner of the Software and associated intellectual property except as expressly licensed here.
        """;

    public const string PrivacySummary = """
        Privacy summary (Windows desktop)
        Data controller: Georgios Kourentzis · caveaipro@gmail.com

        • Local-first: survey JSON/ZIP files you open stay on your PC unless you export or use Push to Cloud.
        • Cloud generative AI: requires Google sign-in and an active CaveAI Pro subscription or trial; structure masks and prompts are sent to CaveAI cloud services for rendering only when you run AI Render.
        • Push to Cloud / Public Library: uses your Google/Firebase session in WebView2; you control what is uploaded.
        • Diagnostics: startup.log and last-error.txt may be written locally under %LOCALAPPDATA%\CaveAiProForWindows\.
        • Updates: optional check against GitHub Releases (Velopack); no personal survey content is sent.
        • GDPR rights: contact caveaipro@gmail.com · Hellenic DPA: www.dpa.gr

        Full privacy policy: www.caveaipro.com/privacy
        """;
}
