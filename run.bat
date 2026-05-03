@echo off
setlocal
set "PF=%ProgramFiles%\CaveAiPro\CaveAiProForWindows\CaveAiProForWindows.exe"
set "PF86=%ProgramFiles(x86)%\CaveAiPro\CaveAiProForWindows\CaveAiProForWindows.exe"
set "LOCAL=%LOCALAPPDATA%\Programs\CaveAiProForWindows\CaveAiProForWindows.exe"
if exist "%PF%" start "" "%PF%" & exit /b 0
if exist "%PF86%" start "" "%PF86%" & exit /b 0
if exist "%LOCAL%" start "" "%LOCAL%" & exit /b 0
echo.
echo CAVE AI PRO is not installed (PC desktop app; not from Google Play).
echo - Recommended: run BuildInstaller.bat, then double-click installer\bin\Release\CaveAiProForWindows-Setup.msi
echo - Or: run Install.bat for a per-user copy under LocalAppData
echo.
pause
exit /b 1
endlocal
