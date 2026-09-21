@echo off
cd /d "%~dp0"
set RENDER=true
echo [DEBUG] RENDER=%RENDER%
pause
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\start-local.ps1"
pause