[CmdletBinding()]
param([string]$DatabasePath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$dll=Join-Path $root 'server\src\NaverSabermetrics.Web\bin\Release\net8.0\NaverSabermetrics.Web.dll'
$config=Join-Path $root 'local-settings.json'
if(!$DatabasePath -and (Test-Path $config)) { $DatabasePath=(Get-Content $config -Raw | ConvertFrom-Json).Site.DatabasePath }
if(!$DatabasePath) { $DatabasePath=Read-Host 'Web snapshot DB path (not the actively written desktop DB)' }
$DatabasePath=$DatabasePath.Trim('"')
$logs=Join-Path $root ('artifacts\real-db-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $logs -Force | Out-Null
& $dotnet build (Join-Path $root 'NaverSabermetrics.Web.sln') -c Release 2>&1 | Tee-Object (Join-Path $logs 'build.log')
if($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $dotnet $dll --check-db $DatabasePath 2>&1 | Tee-Object (Join-Path $logs 'db-check.log')
if($LASTEXITCODE -ne 0) { throw 'DB audit failed. See db-check.log.' }
& $dotnet $dll --compare $DatabasePath 2>&1 | Tee-Object (Join-Path $logs 'parity.log')
if($LASTEXITCODE -ne 0) { throw 'V3 adapter parity failed. See parity.log.' }
Write-Host "REAL_DB_PARITY_PASS. Logs: $logs. Check browser layout and timings separately."
