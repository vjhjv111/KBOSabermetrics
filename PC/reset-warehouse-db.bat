@echo off
setlocal
set "DBDIR=%LOCALAPPDATA%\NaverSabermetrics\Data"
set "DB=%DBDIR%\sabermetrics_v2.db"

echo.
echo [주의] 다음 Naver Sabermetrics V2 관계형 SQLite DB를 삭제하고 새로 만들 준비를 합니다.
echo %DB%
echo.
choice /C YN /N /M "계속할까요? (Y/N): "
if errorlevel 2 goto :cancel

if exist "%DB%" del /F /Q "%DB%"
if exist "%DB%-wal" del /F /Q "%DB%-wal"
if exist "%DB%-shm" del /F /Q "%DB%-shm"

echo.
echo 삭제 완료. 다음 실행에서 새 DB가 생성됩니다.
echo 원본 JSON/ZIP을 한 번 선택해 다시 가져오세요.
pause
exit /b 0

:cancel
echo 취소했습니다.
pause
exit /b 1
