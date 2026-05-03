@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET 8 SDK required: https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
set "OUT=%~dp0dist\TryRun"
if not exist "%OUT%\CaveAiProForWindows.dll" (
  echo Publishing self-contained folder ^(not single-file — more stable for WPF^)...
  dotnet publish "src\CaveAiProForWindows\CaveAiProForWindows.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -o "%OUT%"
  if errorlevel 1 ( pause & exit /b 1 )
)
REM Release exe must see a registered install; TryRun folder is dev-only (same as env in docs).
set "CAVEAI_DEV_SKIP_INSTALL_CHECK=1"
start "" "%OUT%\CaveAiProForWindows.exe"
endlocal
