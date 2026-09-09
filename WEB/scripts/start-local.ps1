[CmdletBinding()]
param([string]$DatabasePath, [switch]$Sample)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$dll=Join-Path $root 'server\src\NaverSabermetrics.Web\bin\Release\net8.0\NaverSabermetrics.Web.dll'
$config=Join-Path $root 'local-settings.json'
Push-Location $root
try {
    & $dotnet build (Join-Path $root 'NaverSabermetrics.Web.sln') -c Release
    if($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & (Join-Path $PSScriptRoot 'check-static-assets.ps1') -ProjectRoot $root -Configuration Release
    if(!(Test-Path $config) -or $Sample -or $DatabasePath) {
        New-Item -ItemType Directory -Path (Join-Path $root 'data') -Force | Out-Null
        $stamp=(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)
        $target=Join-Path $root "data\web-$stamp.db"
        if($Sample) {
            & $dotnet $dll --sample-db (Join-Path $root 'SampleData\2026.zip') $target
        } else {
            if(!$DatabasePath) { $DatabasePath=Read-Host 'Desktop sabermetrics_v2.db full path (a COPY will be made)' }
            $DatabasePath=$DatabasePath.Trim('"')
            & $dotnet $dll --prepare $DatabasePath $target
        }
        if($LASTEXITCODE -ne 0) { throw 'DB preparation failed. Source DB was not modified.' }
        $settings=@{AllowedHosts='localhost;127.0.0.1';Site=@{DatabasePath=$target;StateDirectory=(Join-Path $root 'state');ShowWar=$true;Demo=[bool]$Sample;AllowDevelopmentGuest=$false;Users=@()}}
        $settings | ConvertTo-Json -Depth 6 | Set-Content $config -Encoding UTF8
        Write-Host 'Saved local config outside wwwroot. Do not upload/share local-settings.json or state/.'
    }
    $cfg=Get-Content $config -Raw | ConvertFrom-Json
    $env:SABER_LOCAL_CONFIG=$config; $env:NAVER_SABERMETRICS_DB=$cfg.Site.DatabasePath
    $env:ASPNETCORE_ENVIRONMENT='Development'; $env:DOTNET_ENVIRONMENT='Development'
    Write-Host 'Open http://127.0.0.1:5080. No login is required. Ctrl+C stops the server.'
    & $dotnet $dll --urls 'http://127.0.0.1:5080'
} finally { Pop-Location }
