@echo off
setlocal
cd /d "%~dp0"

set "SMOKE_OUT=%CD%\validation\smoke-output"

if exist "%SMOKE_OUT%" rd /s /q "%SMOKE_OUT%"

echo [0/4] .NET SDK information
dotnet --info
if errorlevel 1 goto :error

echo.
echo [1/4] Restoring packages...
dotnet restore NaverSabermetrics.V2.sln
if errorlevel 1 goto :error

echo.
echo [2/4] Building V2 solution...
dotnet build NaverSabermetrics.V2.sln -c Release --no-restore
if errorlevel 1 goto :error

echo.
echo [3/4] Running known 7-game parser validation...
dotnet run --project src\NaverRelay.Cli\NaverRelay.Cli.csproj -c Release --no-build -- SampleData\2026.zip "%SMOKE_OUT%" --validate-known-sample
if errorlevel 1 goto :error

echo.
echo [4/4] Cleaning smoke output...
if exist "%SMOKE_OUT%" rd /s /q "%SMOKE_OUT%"

echo.
echo V2 verification completed successfully.
pause
exit /b 0

:error
echo.
echo V2 verification failed.
echo Copy the complete error list or console output for diagnosis.
pause
exit /b 1
