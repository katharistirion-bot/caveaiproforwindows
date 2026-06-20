# Cross-repo smoke — Android unit tests, web build/tests, Windows dotnet test/build.
# Exit non-zero on any failure. No deploy.
param(
    [switch]$SkipAndroid,
    [switch]$SkipWeb,
    [switch]$SkipWindows,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = 'Stop'

$androidRoot = 'D:\CaveAIPro'
$webRoot = 'D:\CaveAIpro website'
$windowsRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Step {
    param(
        [string]$Label,
        [scriptblock]$Action
    )
    Write-Host ""
    Write-Host "=== $Label ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed (exit $LASTEXITCODE)"
    }
}

$failed = $false
$results = @()

try {
    if (-not $SkipAndroid) {
        if (-not (Test-Path $androidRoot)) {
            throw "Android repo not found: $androidRoot"
        }
        Push-Location $androidRoot
        try {
            Invoke-Step 'Android unit tests (testDebugUnitTest)' {
                .\gradlew.bat testDebugUnitTest --no-daemon -q
            }
            Invoke-Step 'Android compileDebugKotlin' {
                .\gradlew.bat compileDebugKotlin --no-daemon -q
            }
            $results += 'Android: testDebugUnitTest + compileDebugKotlin OK'
        }
        finally {
            Pop-Location
        }
    }
    else {
        $results += 'Android: skipped'
    }

    if (-not $SkipWeb) {
        if (-not (Test-Path $webRoot)) {
            throw "Web repo not found: $webRoot"
        }
        Push-Location $webRoot
        try {
            if (-not (Test-Path 'node_modules')) {
                Invoke-Step 'Web npm ci' {
                    npm ci
                }
            }
            Invoke-Step 'Web unit tests (site-identity + greeting + contract)' {
                $prevEap = $ErrorActionPreference
                $ErrorActionPreference = 'Continue'
                npm run test:site-identity 2>&1 | Out-Host
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                npm run test:cave-ai-greeting 2>&1 | Out-Host
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                node --test scripts/site-identity-contract.test.mjs 2>&1 | Out-Host
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
                $ErrorActionPreference = $prevEap
            }
            if (-not $SkipWebBuild) {
                Invoke-Step 'Web production build' {
                    npm run build
                }
            }
            $results += 'Web: tests' + ($(if ($SkipWebBuild) { ' (build skipped)' } else { ' + build OK' }))
        }
        finally {
            Pop-Location
        }
    }
    else {
        $results += 'Web: skipped'
    }

    if (-not $SkipWindows) {
        Push-Location $windowsRoot
        try {
            $testProj = Join-Path $windowsRoot 'tests/CaveAiProForWindows.Tests/CaveAiProForWindows.Tests.csproj'
            $appProj = Join-Path $windowsRoot 'src/CaveAiProForWindows/CaveAiProForWindows.csproj'
            Invoke-Step 'Windows dotnet test' {
                dotnet test $testProj -c Debug --verbosity minimal
            }
            Invoke-Step 'Windows dotnet build' {
                dotnet build $appProj -c Debug --verbosity minimal
            }
            $results += 'Windows: dotnet test + build OK'
        }
        finally {
            Pop-Location
        }
    }
    else {
        $results += 'Windows: skipped'
    }
}
catch {
    $failed = $true
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host '--- smoke-all summary ---' -ForegroundColor Yellow
foreach ($line in $results) {
    Write-Host "  $line"
}

if ($failed) {
    exit 1
}

Write-Host ""
Write-Host 'All smoke steps passed.' -ForegroundColor Green
exit 0
