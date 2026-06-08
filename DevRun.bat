@echo off
setlocal
REM Release builds require a registered install (Debug skips InstallationGuard).
cd /d "%~dp0"

REM Stale CaveAiProForWindows.exe / dotnet host locks the build output and can leave an old DLL running.
taskkill /IM CaveAiProForWindows.exe /F >nul 2>&1
taskkill /IM dotnet.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Building Debug...
dotnet build "src\CaveAiProForWindows\CaveAiProForWindows.csproj" -c Debug
if errorlevel 1 (
  pause
  exit /b 1
)

echo Starting CAVE AI PRO...
dotnet run --project "src\CaveAiProForWindows\CaveAiProForWindows.csproj" -c Debug --no-build
endlocal
