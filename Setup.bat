@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo Building CAVE AI PRO setup (Release MSI, self-contained x64)...
dotnet build "installer\CaveAiProForWindows.Installer.wixproj" -c Release
if errorlevel 1 (
  pause
  exit /b 1
)

set "MSI=%~dp0installer\bin\Release\CaveAiProForWindows-Setup.msi"
if not exist "%MSI%" (
  echo MSI not found: %MSI%
  pause
  exit /b 1
)

echo.
echo Setup package ready:
echo   %MSI%
echo   %~dp0_build_out\CaveAiProForWindows-Setup.msi
echo.
echo Starting the Windows Installer wizard (Administrator approval may be required)...
start "" "%MSI%"
endlocal
