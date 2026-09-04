# Fail if Windows firebase/firestore.rules is not the intentional STUB,
# and fail if firebase.json would actually deploy those stub rules.
$ErrorActionPreference = 'Stop'
$firebaseDir = (Join-Path (Join-Path $PSScriptRoot '..') 'firebase') | Resolve-Path
$rulesPath = Join-Path $firebaseDir 'firestore.rules'
$content = Get-Content -LiteralPath $rulesPath -Raw

if ($content -notmatch 'STUB ONLY') {
    Write-Error 'firebase/firestore.rules is not marked STUB ONLY. Deploy from website: npm run deploy:rules'
}

if ($content -match 'desktop_client_telemetry') {
    Write-Error 'Windows firestore.rules looks like full production rules - use website/Android canonical copy only.'
}

$firebaseJsonPath = Join-Path $firebaseDir 'firebase.json'
$firebaseJson = Get-Content -LiteralPath $firebaseJsonPath -Raw | ConvertFrom-Json
if ($null -ne $firebaseJson.firestore) {
    Write-Error 'firebase.json must not include a firestore deploy target. Stub rules in this repo must never ship. Deploy canonical rules from the website or Android repo.'
}

Write-Host 'verify-firestore-rules-stub: OK (stub rules; firestore is not a deploy target)'
