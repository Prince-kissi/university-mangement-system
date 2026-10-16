@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-UMaT.ps1"
if errorlevel 1 (
  echo.
  echo The system could not be opened. Review the error above.
  pause
)
