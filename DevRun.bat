@echo off
setlocal
REM Release builds require a registered install OR this flag (see InstallationGuard).
set "CAVEAI_DEV_SKIP_INSTALL_CHECK=1"
cd /d "%~dp0"
dotnet run --project "src\CaveAiProForWindows\CaveAiProForWindows.csproj" -c Debug
endlocal
