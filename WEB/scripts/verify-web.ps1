[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$work = Join-Path $root "artifacts\verify-$stamp"
$logs = Join-Path $work 'logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$dll = Join-Path $root 'server\src\NaverSabermetrics.Web\bin\Release\net8.0\NaverSabermetrics.Web.dll'
$server = $null
$keys = @('ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','SABER_LOCAL_CONFIG','NAVER_SABERMETRICS_DB','Site__StateDirectory','Site__RequestsPerMinute','Site__IpRequestsPerMinute','Site__DailyQueries','Site__DailyRows','Site__AllowDevelopmentGuest','Site__Demo','Site__ShowWar')
$saved = @{}
foreach($key in $keys) { $saved[$key] = [Environment]::GetEnvironmentVariable($key,'Process') }
function Run-Dotnet([string]$name, [string[]]$arguments) {
    Write-Host "`n=== $name ==="
    & $dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $logs "$name.log")
    if($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE" }
}
function New-Port {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $p = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop(); return $p
}
function Start-TestServer([string]$name,[int]$port) {
    $process = Start-Process -FilePath $dotnet -ArgumentList @(('"' + $dll + '"'), '--urls', "http://127.0.0.1:$port") -WorkingDirectory $root -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $logs "$name.stdout.log") -RedirectStandardError (Join-Path $logs "$name.stderr.log")
    for($i=0;$i -lt 120;$i++) {
        if($process.HasExited) {
            $process.WaitForExit()
            Write-Host "`n=== $name startup stderr ===" -ForegroundColor Red
            $stderr = Join-Path $logs "$name.stderr.log"
            if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr -Tail 60 | ForEach-Object { Write-Host $_ } }
            throw "Server exited (code $($process.ExitCode)). See $name stderr log."
        }
        try { $r=Invoke-RestMethod "http://127.0.0.1:$port/api/health" -TimeoutSec 1; if($r.status -eq 'ok') { return $process } } catch {}
        Start-Sleep -Milliseconds 500
    }
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw 'Server did not become ready within 60 seconds.'
}
try {
    Push-Location $root
    Run-Dotnet '01-sdk' @('--info')
    Run-Dotnet '02-restore' @('restore',(Join-Path $root 'NaverSabermetrics.Web.sln'))
    Run-Dotnet '03-build' @('build',(Join-Path $root 'NaverSabermetrics.Web.sln'),'-c','Release','--no-restore')
    & (Join-Path $PSScriptRoot 'check-static-assets.ps1') -ProjectRoot $root -Configuration Release |
        Tee-Object -FilePath (Join-Path $logs '03b-static-assets.log')
    $db = Join-Path $work 'sample.db'
    Run-Dotnet '04-sample-import' @($dll,'--sample-db',(Join-Path $root 'SampleData\2026.zip'),$db)
    Run-Dotnet '05-schema-integrity' @($dll,'--check-db',$db)
    Run-Dotnet '06-adapter-parity-unit' @($dll,'--self-test',$db)
    $env:SABER_LOCAL_CONFIG=$null; $env:NAVER_SABERMETRICS_DB=$db
    $env:ASPNETCORE_ENVIRONMENT='Development'; $env:DOTNET_ENVIRONMENT='Development'
    $env:Site__AllowDevelopmentGuest='false'; $env:Site__Demo='false'; $env:Site__ShowWar='true'
    $env:Site__RequestsPerMinute='10000'; $env:Site__IpRequestsPerMinute='10000'
    $env:Site__DailyQueries='10000'; $env:Site__DailyRows='1000000'
    $env:Site__StateDirectory=Join-Path $work 'state'
    $before = (Get-FileHash $db -Algorithm SHA256).Hash
    $port=New-Port; $server=Start-TestServer '07-server' $port
    Run-Dotnet '08-http-integration' @($dll,'--http-check',"http://127.0.0.1:$port")
    Stop-Process -Id $server.Id -Force; $server=$null
    $env:Site__RequestsPerMinute='3'
    $port=New-Port; $server=Start-TestServer '09-rate-server' $port
    Run-Dotnet '10-rate-limit' @($dll,'--http-check-rate',"http://127.0.0.1:$port")
    Stop-Process -Id $server.Id -Force; $server=$null
    if((Get-FileHash $db -Algorithm SHA256).Hash -ne $before) { throw 'HTTP queries changed the readonly database.' }
    'ALL_EXECUTED_CHECKS_PASS' | Set-Content (Join-Path $logs 'RESULT.txt') -Encoding UTF8
    Write-Host "`nALL_EXECUTED_CHECKS_PASS" -ForegroundColor Green
} catch {
    "FAIL: $_" | Tee-Object -FilePath (Join-Path $logs 'RESULT.txt')
    $failed = $true
} finally {
    if($server -and !$server.HasExited) { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
    foreach($key in $keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key],'Process') }
    Pop-Location
    # Only logs are collected. NO DB, passwords, config, keys or quota state in this ZIP.
    $bundle = Join-Path $work 'verification-logs.zip'
    Compress-Archive -Path (Join-Path $logs '*') -DestinationPath $bundle -Force
    Write-Host "Logs: $bundle"
}
if($failed) { exit 1 }
