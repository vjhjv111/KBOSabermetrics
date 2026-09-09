@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Restoring packages...
dotnet restore NaverSabermetrics.V2.sln
if errorlevel 1 goto :error

echo [2/2] Building solution...
dotnet build NaverSabermetrics.V2.sln -c Release --no-restore
if errorlevel 1 goto :error

echo.
echo Build completed successfully.
pause
exit /b 0

:error
echo.
echo Build failed. Install the .NET 8 SDK and the Visual Studio '.NET desktop development' workload.
pause
exit /b 1
