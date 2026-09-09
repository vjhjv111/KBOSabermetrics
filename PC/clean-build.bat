@echo off
setlocal
cd /d "%~dp0"

for /d /r %%D in (bin,obj) do @if exist "%%D" rd /s /q "%%D"

dotnet restore NaverSabermetrics.V2.sln
if errorlevel 1 goto :error

dotnet build NaverSabermetrics.V2.sln -c Release --no-restore
if errorlevel 1 goto :error

echo.
echo Clean build completed successfully.
pause
exit /b 0

:error
echo.
echo Build failed. Copy the complete error list and send it for diagnosis.
pause
exit /b 1
