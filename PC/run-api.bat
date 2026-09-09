@echo off
setlocal
cd /d "%~dp0"

echo Naver Sabermetrics API starting...
echo Default URL: http://localhost:5080
set ASPNETCORE_URLS=http://localhost:5080
dotnet run --project src\NaverRelay.Api\NaverRelay.Api.csproj
if errorlevel 1 pause
