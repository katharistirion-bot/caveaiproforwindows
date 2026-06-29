# Fail if Windows firebase/firestore.rules is not the intentional STUB (prevents accidental deploy).
$ErrorActionPreference = 'Stop'
$rulesPath = (Join-Path (Join-Path $PSScriptRoot '..') 'firebase\firestore.rules') | Resolve-Path
$content = Get-Content -LiteralPath $rulesPath -Raw

if ($content -notmatch 'STUB ONLY') {
    Write-Error 'firebase/firestore.rules is not marked STUB ONLY. Deploy from website: npm run deploy:rules'
}

if ($content -match 'desktop_client_telemetry') {
    Write-Error 'Windows firestore.rules looks like full production rules - use website/Android canonical copy only.'
}

Write-Host 'verify-firestore-rules-stub: OK (stub rules; do not deploy firestore:rules from this repo)'