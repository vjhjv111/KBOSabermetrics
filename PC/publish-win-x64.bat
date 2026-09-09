@echo off
setlocal
cd /d "%~dp0"
dotnet publish src\NaverRelay.Gui\NaverRelay.Gui.csproj -c Release -r win-x64 --self-contained false -o publish\win-x64
if errorlevel 1 goto :error

echo.
echo Published to: %CD%\publish\win-x64
pause
exit /b 0

:error
echo.
echo Publish failed.
pause
exit /b 1
