# Sign MSI (placeholder)
param(
    [Parameter(Mandatory = $true)][string]$MsiPath,
    [Parameter(Mandatory = $true)][string]$PfxPath,
    [string]$SignTool = "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe",
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

if (-not (Test-Path $SignTool)) {
    Write-Error "signtool not found at $SignTool — install Windows SDK or set -SignTool."
    exit 1
}
if (-not (Test-Path $MsiPath)) {
    Write-Error "MSI not found: $MsiPath"
    exit 1
}
if (-not (Test-Path $PfxPath)) {
    Write-Error "PFX not found: $PfxPath — see installer/SIGNING.md"
    exit 1
}

$secure = Read-Host "PFX password" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

& $SignTool sign /fd SHA256 /f $PfxPath /p $plain /tr $TimestampUrl /td SHA256 $MsiPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $SignTool verify /pa /v $MsiPath
