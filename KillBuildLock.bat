@echo off
REM Stops the app + Cursor/VS Code C# debugger so MSBuild can copy CaveAiProForWindows.dll (fixes MSB3026 / file lock).
taskkill /F /IM CaveAiProForWindows.exe >nul 2>&1
taskkill /F /IM netcoredbg.exe >nul 2>&1
echo Unlocked. Run build or F5 again.
exit /b 0
