@echo off
setlocal
cd /d "%~dp0"

where py >nul 2>nul
if not errorlevel 1 (
  py -3 validation\run_all_v2_checks.py
  goto :done
)

where python >nul 2>nul
if not errorlevel 1 (
  python validation\run_all_v2_checks.py
  goto :done
)

echo Python 3 was not found.
echo Use verify-v2.bat for the .NET build and parser validation.
exit /b 1

:done
if errorlevel 1 (
  echo.
  echo Static validation failed.
  pause
  exit /b 1
)

echo.
echo Static validation completed successfully.
pause
exit /b 0
