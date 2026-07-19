@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-AgentCake.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed.
  pause
  exit /b %errorlevel%
)
echo.
echo AgentCake installed successfully.
pause
