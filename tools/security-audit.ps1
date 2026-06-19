# Pre-release security audit - scans git-tracked sources and optional build outputs for exposed secrets.
# Run from repo root:  .\tools\security-audit.ps1
# Exit 1 on any FAIL; warnings are advisory unless -Strict.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AndroidRoot = (Join-Path (Split-Path -Parent $RepoRoot) 'CaveAIPro'),
    [string]$WebsiteRoot = (Join-Path (Split-Path -Parent $RepoRoot) 'CaveAIpro website'),
    [string]$BrowserKeySuffix = 'iFyD8',
    [string]$AndroidKeySuffix = 'RRRmA0',
    [switch]$CheckBuildOutputs,
    [switch]$CheckAndroidRemote,
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'

$script:FailCount = 0
$script:WarnCount = 0

function Write-Pass([string]$Message) { Write-Host "PASS: $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) {
    Write-Host "WARN: $Message" -ForegroundColor Yellow
    $script:WarnCount++
}
function Write-Fail([string]$Message) {
    Write-Host "FAIL: $Message" -ForegroundColor Red
    $script:FailCount++
}

function Test-GitRepo([string]$Path) {
    if (-not (Test-Path -LiteralPath (Join-Path $Path '.git'))) { return $false }
    git -C $Path rev-parse --is-inside-work-tree 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-GitTrackedFiles([string]$Root, [string[]]$Globs) {
    if (-not (Test-GitRepo $Root)) { return @() }
    $args = @('-C', $Root, 'ls-files', '--') + $Globs
    $lines = & git @args 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    return $lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Test-IsFakeFirebaseKey([string]$Key) {
    $fakePrefixes = @(
        'AIzaSyExample',
        'AIzaSyTest',
        'AIzaSyObserved',
        'AIzaSyValid',
        'AIzaSyPush'
    )
    foreach ($p in $fakePrefixes) {
        if ($Key.StartsWith($p, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Find-FirebaseKeysInText([string]$Text) {
    [regex]::Matches($Text, 'AIzaSy[A-Za-z0-9_-]{20,}') | ForEach-Object { $_.Value }
}

Write-Host '=== CAVE AI PRO security audit ===' -ForegroundColor Cyan
Write-Host "Windows repo: $RepoRoot"

# --- 1. Git-tracked source must use placeholder ---
$sourceConfig = Join-Path $RepoRoot 'src\CaveAiProForWindows\Assets\DesktopAuth\firebase-config.json'
if (-not (Test-Path -LiteralPath $sourceConfig)) {
    Write-Fail "Missing $sourceConfig"
} elseif ((Get-Content -LiteralPath $sourceConfig -Raw) -notmatch 'REPLACE_AT_BUILD') {
    Write-Fail "Git-tracked firebase-config.json must contain REPLACE_AT_BUILD (run restore-firebase-config-placeholder.ps1)"
} else {
    Write-Pass 'Windows source firebase-config.json uses REPLACE_AT_BUILD'
}

# --- 2. tools/local secrets must not be git-tracked ---
$localAllowed = @(
    'tools/local/.gitkeep',
    'tools/local/README.md',
    'tools/local/firebase-web-config.json.example'
)
$localTracked = Get-GitTrackedFiles $RepoRoot @('tools/local/*')
$localBad = $localTracked | Where-Object { $_ -notin $localAllowed }
if ($localBad.Count -gt 0) {
    foreach ($f in $localBad) { Write-Fail "Secret file is git-tracked: $f" }
} else {
    Write-Pass 'tools/local/ contains only safe tracked files (example + README)'
}

# --- 3. Scan Windows git-tracked text for real API keys ---
$aizaExcludePaths = @(
    'tests/CaveAiProForWindows.Tests/FirebaseAuthInjectionRegressionTests.cs',
    'tests/CaveAiProForWindows.Tests/FirebaseProjectConfigTests.cs',
    'tools/verify-android-google-services.ps1'
)
$scanGlobs = @('*.cs', '*.json', '*.js', '*.jsx', '*.ps1', '*.yml', '*.yaml', '*.md', '*.xml', '*.kt', '*.properties')
$tracked = Get-GitTrackedFiles $RepoRoot $scanGlobs
foreach ($rel in $tracked) {
    if ($rel -in $aizaExcludePaths) { continue }
    $full = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
    $text = Get-Content -LiteralPath $full -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }
    foreach ($key in (Find-FirebaseKeysInText $text)) {
        if (Test-IsFakeFirebaseKey $key) { continue }
        Write-Fail "Firebase API key in git-tracked file: $rel (...$($key.Substring($key.Length - 6)))"
    }
    if ($text -match 'sk-[a-zA-Z0-9]{20,}') {
        Write-Fail "OpenAI-style secret in git-tracked file: $rel"
    }
    if ($text -match 'r8_[a-zA-Z0-9]{10,}') {
        Write-Fail "Replicate token in git-tracked file: $rel"
    }
    if ($text -match '-----BEGIN (RSA |EC )?PRIVATE KEY-----') {
        Write-Fail "Private key block in git-tracked file: $rel"
    }
}

if ($script:FailCount -eq 0) {
    Write-Pass 'No real API keys / tokens in Windows git-tracked sources (tests excluded)'
}

# --- 4. Cross-key contamination in Windows git ---
$androidSuffixExcludePaths = @(
    'tools/verify-android-google-services.ps1',
    'tools/security-audit.ps1',
    'docs/SECURITY-HARDENING.md'
)
$androidSuffixPattern = [regex]::Escape($AndroidKeySuffix)
foreach ($rel in $tracked) {
    if ($rel -in $androidSuffixExcludePaths) { continue }
    $full = Join-Path $RepoRoot $rel
    $text = Get-Content -LiteralPath $full -Raw -ErrorAction SilentlyContinue
    if ($text -and $text -match $androidSuffixPattern) {
        Write-Fail "Android Firebase key suffix (...$AndroidKeySuffix) in Windows git file: $rel"
    }
}

if ($script:FailCount -eq 0) {
    Write-Pass "Android key suffix (...$AndroidKeySuffix) not in Windows git sources"
}

# --- 5. Android repo git hygiene ---
if (Test-GitRepo $AndroidRoot) {
    Write-Host "Android repo: $AndroidRoot"
    $androidSensitive = @('local.properties', 'keystore.properties', 'app/google-services.json', 'upload-keystore.jks')
    foreach ($rel in $androidSensitive) {
        $listed = Get-GitTrackedFiles $AndroidRoot @($rel)
        if ($listed.Count -gt 0) {
            Write-Fail "Android secret is git-tracked: $rel"
        }
    }
    if ($script:FailCount -eq 0) {
        Write-Pass 'Android sensitive files are not git-tracked'
    }

    $androidTracked = Get-GitTrackedFiles $AndroidRoot $scanGlobs
    foreach ($rel in $androidTracked) {
        $full = Join-Path $AndroidRoot $rel
        $text = Get-Content -LiteralPath $full -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrEmpty($text)) { continue }
        foreach ($key in (Find-FirebaseKeysInText $text)) {
            if (Test-IsFakeFirebaseKey $key) { continue }
            Write-Fail "Firebase API key in Android git-tracked file: $rel"
        }
    }

    if ($CheckAndroidRemote) {
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        $remoteRefs = & git -C $AndroidRoot ls-remote --heads origin master main 2>&1
        if ($LASTEXITCODE -eq 0 -and $remoteRefs) {
            $foundOnRemote = $false
            foreach ($rel in @('keystore.properties', 'app/google-services.json', 'local.properties')) {
                & git -C $AndroidRoot show "origin/master:$rel" 2>&1 | Out-Null
                if ($LASTEXITCODE -eq 0) {
                    Write-Fail "Android secret exists on origin/master: $rel"
                    $foundOnRemote = $true
                }
            }
            if (-not $foundOnRemote) {
                Write-Pass 'Android origin/master has no keystore / google-services / local.properties'
            }
        } else {
            Write-Warn 'Android remote not reachable - skipped origin secret check'
        }
        $ErrorActionPreference = $prevEap
    }

    $keystoreProps = Join-Path $AndroidRoot 'keystore.properties'
    if (Test-Path -LiteralPath $keystoreProps) {
        $kp = Get-Content -LiteralPath $keystoreProps -Raw
        if ($kp -match 'TEMP|change-me|REPLACE_ME') {
            Write-Warn 'keystore.properties still uses placeholder/TEMP passwords - rotate before long-term signing'
        }
    }
    if (Test-Path -LiteralPath (Join-Path $AndroidRoot 'local.properties')) {
        Write-Warn 'local.properties exists on disk (expected) - never git add; verify Cloud Console API restrictions'
    }
} else {
    Write-Warn "Android repo not found at $AndroidRoot - skipped Android checks"
}

# --- 6. Website key discipline (disk; may not be a git repo) ---
$webFirebase = Join-Path $WebsiteRoot 'src\firebase.js'
if (Test-Path -LiteralPath $webFirebase) {
    $wf = Get-Content -LiteralPath $webFirebase -Raw
    if ($wf -match [regex]::Escape($AndroidKeySuffix)) {
        Write-Fail "Website firebase.js contains Android key suffix (...$AndroidKeySuffix) - use Browser key only"
    } elseif ($wf -match [regex]::Escape($BrowserKeySuffix)) {
        Write-Pass "Website firebase.js uses Browser key suffix (...$BrowserKeySuffix)"
    } else {
        Write-Warn 'Website firebase.js apiKey suffix not recognized - verify Browser vs Android key manually'
    }
    $envLocal = Join-Path $WebsiteRoot '.env.local'
    if (Test-Path -LiteralPath $envLocal) {
        Write-Warn '.env.local exists on disk (gitignored) - do not commit'
    }
} else {
    Write-Warn "Website not found at $WebsiteRoot - skipped website checks"
}

# --- 7. Optional build outputs ---
if ($CheckBuildOutputs) {
    $buildConfig = Join-Path $RepoRoot '_build_out\Assets\DesktopAuth\firebase-config.json'
    if (Test-Path -LiteralPath $buildConfig) {
        $bc = Get-Content -LiteralPath $buildConfig -Raw
        if ($bc -match 'REPLACE_AT_BUILD') {
            Write-Fail '_build_out firebase-config still has REPLACE_AT_BUILD - inject before ship'
        } elseif ($bc -match [regex]::Escape($AndroidKeySuffix)) {
            Write-Fail "_build_out contains Android key - must be Browser key (...$BrowserKeySuffix)"
        } elseif ($bc -match [regex]::Escape($BrowserKeySuffix)) {
            Write-Pass "_build_out uses Browser key suffix (...$BrowserKeySuffix)"
        } else {
            Write-Warn '_build_out firebase-config has unknown key suffix - clean rebuild or revoke old key in Console'
        }
    }
}

# --- 8. Firestore rules canonical (sibling repos) ---
$rulesScript = Join-Path $RepoRoot 'tools\verify-firestore-rules-canonical.ps1'
if (Test-Path -LiteralPath $rulesScript) {
    & powershell -NoProfile -File $rulesScript
    if ($LASTEXITCODE -ne 0) {
        Write-Fail 'verify-firestore-rules-canonical.ps1 failed'
    }
}

# --- Summary ---
Write-Host ''
Write-Host "=== Summary: $($script:FailCount) FAIL, $($script:WarnCount) WARN ===" -ForegroundColor Cyan
if ($Strict -and $script:WarnCount -gt 0) {
    Write-Host 'Strict mode: treating warnings as failures.' -ForegroundColor Yellow
    exit 1
}
if ($script:FailCount -gt 0) {
    exit 1
}
exit 0
