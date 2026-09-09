@echo off
setlocal
cd /d "%~dp0"
dotnet run --project src\NaverRelay.Gui\NaverRelay.Gui.csproj
if errorlevel 1 pause
