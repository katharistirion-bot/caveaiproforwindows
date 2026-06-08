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
REM Register TryRun folder as a dev install (Release builds no longer honor env bypass).
reg add "HKCU\SOFTWARE\CaveAiPro\CaveAiProForWindows" /v Installed /t REG_DWORD /d 1 /f >nul
reg add "HKCU\SOFTWARE\CaveAiPro\CaveAiProForWindows" /v InstallDir /t REG_SZ /d "%OUT%" /f >nul
start "" "%OUT%\CaveAiProForWindows.exe"
endlocal
