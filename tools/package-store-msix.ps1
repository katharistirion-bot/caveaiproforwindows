# Builds an unsigned MSIX for Microsoft Store submission.
# Pipeline: MicrosoftStore-Win64 publish (Obfuscar + self-contained) -> verify -> makeappx pack (no signing).
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$IdentityName = 'GeorgiosKourentzis.CaveAIPro',
    [string]$Publisher = 'CN=54966508-95FA-45A0-B2A2-D1AF31D44DC4',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
Set-Location $RepoRoot

function Get-VersionFromProps {
    param([string]$PropsPath)
    [xml]$xml = Get-Content -LiteralPath $PropsPath
    return $xml.Project.PropertyGroup.Version
}

function Get-MakeAppxPath {
    param([string]$RepoRoot)
    $kitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot) {
        $kitsVer = Get-ChildItem -LiteralPath $kitsRoot -Directory |
            Where-Object { $_.Name -match '^\d' } |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1
        if ($kitsVer) {
            $candidate = Join-Path $kitsVer.FullName 'x64\makeappx.exe'
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }

    $toolProj = Join-Path $RepoRoot 'store/msix-tool.csproj'
    dotnet restore $toolProj --verbosity quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore store/msix-tool.csproj failed.' }

    $nugetRoot = Join-Path $RepoRoot 'store/obj/nuget'
    $makeappx = Get-ChildItem -LiteralPath (Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools') -Recurse -Filter 'makeappx.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\makeappx\.exe$' } |
        Select-Object -First 1
    if (-not $makeappx) {
        throw @'
makeappx.exe not found. Install Windows 10/11 SDK (Desktop development with C++)
or ensure store/msix-tool.csproj restores Microsoft.Windows.SDK.BuildTools.
'@
    }
    return $makeappx.FullName
}

function Ensure-StoreAssets {
    param([string]$AssetsDir, [string]$LogoSource)
    if (-not (Test-Path -LiteralPath $LogoSource)) {
        throw "Logo source not found for Store assets: $LogoSource"
    }
    New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null
    $targets = @(
        'StoreLogo.png',
        'Square44x44Logo.png',
        'Square150x150Logo.png'
    )
    foreach ($name in $targets) {
        Copy-Item -LiteralPath $LogoSource -Destination (Join-Path $AssetsDir $name) -Force
    }
}

$version = Get-VersionFromProps (Join-Path $RepoRoot 'Directory.Build.props')
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Could not read <Version> from Directory.Build.props.' }

$packageVersion = if ($version -match '^\d+\.\d+\.\d+$') { "$version.0" } else { $version }
$appProj = Join-Path $RepoRoot 'src/CaveAiProForWindows/CaveAiProForWindows.csproj'
$injectScript = Join-Path $RepoRoot 'tools/inject-firebase-config.ps1'

$verifyScript = Join-Path $RepoRoot 'tools/verify-firebase-config.ps1'
$verifyStoreScript = Join-Path $RepoRoot 'tools/verify-store-publish.ps1'

Write-Host "Version: $version (package $packageVersion)"
Write-Host 'Step 0/5: inject Firebase client config (required for Store MSIX)'
if ([string]::IsNullOrWhiteSpace($env:CAVEAIPRO_FIREBASE_API_KEY)) {
    throw @'
Store MSIX packaging requires CAVEAIPRO_FIREBASE_API_KEY (Firebase Web API key).
Set the environment variable, then re-run package-store-msix.ps1.
See docs/SECURITY.md.
'@
}
& $injectScript -RepoRoot $RepoRoot
if ($LASTEXITCODE -ne 0) { throw 'inject-firebase-config.ps1 failed.' }

Write-Host 'Step 1/5: dotnet restore'
dotnet restore (Join-Path $RepoRoot 'CaveAiProForWindows.sln') --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Host 'Step 2/5: Publish Microsoft Store profile (self-contained, Obfuscar, STORE_DISTRIBUTION)'
# Full publish only — never use --no-build here. A prior framework-dependent build plus
# "dotnet publish --no-build" leaves runtimeconfig.json with "frameworks" instead of
# "includedFrameworks", which triggers the Windows ".NET install" dialog on machines
# without the Desktop Runtime (Store certification 10.1.2.10).
dotnet publish $appProj -c Release -p:PublishProfile=MicrosoftStore-Win64 --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Store publish failed.' }

$pubDir = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/publish/microsoft-store/win-x64'
$exe = Join-Path $pubDir 'CaveAiProForWindows.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Missing published exe: $exe" }

& $verifyStoreScript -PublishDir $pubDir
if ($LASTEXITCODE -ne 0) { throw 'verify-store-publish.ps1 failed — MSIX must be self-contained.' }

$obfLog = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/win-x64/obfuscated/obfuscar.log'
if (-not (Test-Path -LiteralPath $obfLog)) {
    $obfLogAlt = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/obfuscated/obfuscar.log'
    if (Test-Path -LiteralPath $obfLogAlt) { $obfLog = $obfLogAlt }
}
$obfLogInOutput = Join-Path $RepoRoot 'src/CaveAiProForWindows/bin/Release/net8.0-windows/win-x64/obfuscar.log'
if (Test-Path -LiteralPath $obfLog) {
    Write-Host "OK: Obfuscar log $obfLog"
} elseif (Test-Path -LiteralPath $obfLogInOutput) {
    Write-Host "OK: Obfuscar log $obfLogInOutput"
} else {
    Write-Warning 'Obfuscar log not found — verify MSBuild.Obfuscar ran (Release configuration).'
}

$configDest = Join-Path $pubDir 'Assets/DesktopAuth/firebase-config.json'
& $verifyScript -ConfigPath $configDest
if ($LASTEXITCODE -ne 0) { throw 'verify-firebase-config.ps1 failed on Store publish output.' }

Write-Host 'Step 4/5: Stage MSIX layout (trim Velopack — Store handles updates)'
$staging = Join-Path $RepoRoot 'store/obj/msix-staging'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

Copy-Item -Path (Join-Path $pubDir '*') -Destination $staging -Recurse -Force
Get-ChildItem -LiteralPath $staging -Filter '*.pdb' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $staging -Filter 'Velopack*.dll' -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host 'Step 5/5: Pack unsigned MSIX'

$logoSource = Join-Path $RepoRoot 'src/CaveAiProForWindows/Assets/logo.png'
Ensure-StoreAssets -AssetsDir (Join-Path $staging 'Assets') -LogoSource $logoSource

$manifestTemplate = Join-Path $RepoRoot 'store/Package.appxmanifest'
$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest -replace '(<Identity[^>]*\sVersion=")[^"]+(")', "`${1}$packageVersion`${2}"
$manifest = $manifest -replace '(<Identity[^>]*\sName=")[^"]+(")', "`${1}$IdentityName`${2}"
$manifest = $manifest -replace '(<Identity[^>]*\sPublisher=")[^"]+(")', "`${1}$Publisher`${2}"
Set-Content -LiteralPath (Join-Path $staging 'AppxManifest.xml') -Value $manifest -Encoding UTF8

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot '_store_out'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$msixName = "CaveAiProForWindows-$version-Store-unsigned.msix"
$msixPath = Join-Path $OutDir $msixName
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }

$makeappx = Get-MakeAppxPath -RepoRoot $RepoRoot
Write-Host "Using makeappx: $makeappx"
& $makeappx pack /d $staging /p $msixPath /o /nv
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }

$info = Get-Item -LiteralPath $msixPath
$sizeMb = [math]::Round($info.Length / 1MB, 2)
Write-Host ''
Write-Host '=== Store MSIX ready (unsigned) ==='
Write-Host "Path:   $($info.FullName)"
Write-Host "Size:   $($info.Length) bytes ($sizeMb MB)"
Write-Host "Version: $version"
Write-Host 'Signing: none (Partner Center / Store re-signs on upload)'

if ($env:GITHUB_OUTPUT) {
    "msix_path=$($info.FullName)" >> $env:GITHUB_OUTPUT
    "msix_version=$version" >> $env:GITHUB_OUTPUT
}
