Microsoft Store — CAVE AI PRO 1.5.25
====================================
Product ID: 9PPF3HPZRL21
Upload: CaveAiProForWindows-1.5.25-Store-unsigned.msix
Size: 102,314,708 bytes (PK header OK)
Built: 2026-10-19 22:59
Signing: none (NotSigned) — correct for Partner Center; Microsoft re-signs

Do not upload CaveAiProForWindows-1.5.24-Store-unsigned.msix. That package still writes the open map sheet twice.

Partner Center → CAVE AI PRO → Packages → Upload (unsigned OK; Microsoft re-signs)
Confirm package version 1.5.25.0

No Partner Center / StoreBroker CLI credentials are on this machine.
Do not fake an upload — use Partner Center in the browser.
Do not sign this MSIX locally. Do not upload the Review (STORE_REVIEW_UNLOCKED) build.

Release notes (English):
• Saving a survey stores the open map sheet once, so large caves stay smaller and reopen with the drawing.

Pre-upload smoke:
• Open a survey, draw, save, close, and reopen. The drawing is still there.
• Reference catalog Start survey → Cancel unsaved prompt → catalog stays open.
• Sign in → catalog CDN still loads after token refresh.
• WebView Back on Explore keeps embed=windows.

Website www.caveaipro.com is live.
Android AAB: D:\CaveAIPro\_play_upload\CaveAIPro-1.9.10-114.aab
