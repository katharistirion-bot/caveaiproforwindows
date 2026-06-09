# DEPRECATED — do not sync Android google-services.json to website or Windows.
# Android uses a package-restricted API key. Web/Windows need the Browser key.
# Use: .\tools\sync-firebase-web-config.ps1
Write-Error @'
sync-firebase-key-from-android.ps1 is deprecated.
Web and Windows require the Firebase Browser API key (firebase-web-config.json),
not the Android google-services.json key.
Run: .\tools\sync-firebase-web-config.ps1 -InjectWindows
See tools/local/README.md
'@
exit 1
