@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
dotnet build "installer\CaveAiProForWindows.Installer.wixproj" -c Release
if errorlevel 1 (
  pause
  exit /b 1
)
echo.
echo MSI ready ^(Windows 10/11 x64 desktop only; not Google Play^):
echo   %~dp0installer\bin\Release\CaveAiProForWindows-Setup.msi
echo.
echo Double-click the MSI on a Windows PC: standard wizard ^(license, folder, progress bar^), full app copy, Start menu shortcut.
echo Android CaveAI Pro stays on Google Play; this MSI is the separate PC companion.
echo.
pause
endlocal
