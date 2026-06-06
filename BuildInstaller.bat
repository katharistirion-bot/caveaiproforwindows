@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
echo Building CAVE AI PRO setup (Release MSI, self-contained win-x64)...
dotnet build "installer\CaveAiProForWindows.Installer.wixproj" -c Release
if errorlevel 1 (
  pause
  exit /b 1
)
echo.
echo MSI ready (Windows 10/11 x64; per-machine install under Program Files):
echo   %~dp0installer\bin\Release\CaveAiProForWindows-Setup.msi
echo   %~dp0_build_out\CaveAiProForWindows-Setup.msi
echo.
echo Install: double-click the MSI, or run Setup.bat to build and launch the wizard.
echo After install: Start menu + desktop shortcut "CAVE AI PRO".
echo Uninstall: Settings - Apps - CAVE AI PRO.
echo.
pause
endlocal
