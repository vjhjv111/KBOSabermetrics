<#
.SYNOPSIS
  진행 중 JSON 갱신 -> 종료 경기 DB 반영 -> DB 구조 검증 -> Render 내부 증분 반영/배포.
  기본 스케줄은 Register-Task.ps1의 18:00~00:30, 10분 간격입니다.
  공식 시즌 대조는 기본적으로 건너뛰며 -RunSeasonReconcile로 명시한 경우에만 실행합니다.
  -LocalOnly는 웹 봉인까지 실행하며 업로드/서비스 재시작은 생략합니다.
  -FullDatabaseUpload는 원격 증분 대신 전체 웹 DB를 다시 전송하는 복구용 옵션입니다.
  Render 내부 수집기의 최근 성공 heartbeat가 있으면 예약 실행은 전체 로컬 파이프라인을 건너뜁니다.
  -FullDatabaseUpload를 명시하면 heartbeat와 관계없이 복구용 전체 배포를 실행합니다.
  SQLite 무결성·외래키·원격 반영 검증 실패 시 배포하지 않고 다음 주기에 재시도합니다.
#>
param(
    [string]$CollectOutputDir = "$env:USERPROFILE\Documents\NaverKboCombined",
    [string]$DesktopDb = "$env:LOCALAPPDATA\NaverSabermetrics\Data\sabermetrics_v2.db",
    [string]$WebDbDir = "$env:USERPROFILE\Documents\NaverKboCombined\web-db",
    [string]$LogDir = "$env:USERPROFILE\Documents\NaverKboCombined\logs",
    [string]$RepoRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [string]$RelayUIExe,
    [string]$ImportCliExe,
    [string]$WebAppExe,
    [ValidateRange(1,30)][int]$LookbackDays = 1,
    [string]$SshHost = 'srv-dah4arpt0dsc73egvl3g@ssh.singapore.render.com',
    [string]$RemoteDbPath = '/var/data/sabermetrics_v2.db',
    [string]$RemoteToolRoot = '/var/data/kbo-tools',
    [string]$RemoteInboxRoot = '/var/data/kbo-inbox',
    [string]$RenderHealthUrl = 'https://kbosabermetrics.onrender.com/api/health',
    [string]$RemoteToolLocalPath = "$PSScriptRoot\remote-tools\linux-x64\NaverRelay.Cli",
    [string]$RemoteScriptLocalPath = "$PSScriptRoot\Remote-Incremental.sh",
    [string[]]$SshArgs = @('-o','BatchMode=yes','-o','ConnectTimeout=30','-o','ServerAliveInterval=30','-o','ServerAliveCountMax=20'),
    [string]$SshExtraArgs = '',
    [ValidateRange(1,1000)][int]$KeepLocalWebDbCount = 3,
    [string]$RenderServiceId = 'srv-dah4arpt0dsc73egvl3g',
    [string]$RenderApiKey = $env:RENDER_API_KEY,
    [switch]$RunSeasonReconcile,
    [switch]$FullDatabaseUpload,
    [switch]$LocalOnly
)
$ErrorActionPreference = 'Stop'
if (!$RelayUIExe) { $RelayUIExe = Join-Path $RepoRoot 'DATA_SCRAPPER\bin\Release\net8.0-windows\NaverKboRelayUI.exe' }
if (!$ImportCliExe) { $ImportCliExe = Join-Path $RepoRoot 'PC\src\NaverRelay.Cli\bin\Release\net8.0\NaverRelay.Cli.exe' }
if (!$WebAppExe) { $WebAppExe = Join-Path $RepoRoot 'WEB\server\src\NaverSabermetrics.Web\bin\Release\net8.0\NaverSabermetrics.Web.exe' }
New-Item -ItemType Directory -Force -Path $CollectOutputDir,$WebDbDir,$LogDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss_fff'
$logFile = Join-Path $LogDir "pipeline_$stamp.log"
$lockFile = Join-Path $LogDir 'pipeline.lock'
# OS-held lock; released after a crash. Keep the file to avoid a delete/open race.
try { $lock = [IO.File]::Open($lockFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch [IO.IOException] { Write-Host '다른 파이프라인이 실행 중입니다. 이번 주기는 건너뜁니다.'; exit 0 }
function Log([string]$message) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $message"
    Write-Host $line
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}
function Run([string]$exe, [string[]]$arguments, [string]$step) {
    Log $step
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $exe @arguments 2>&1 | ForEach-Object { Log "  $_" }; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "$step 실패 (exit=$code)" }
}
function Run-Capture([string]$exe, [string[]]$arguments, [string]$step) {
    Log $step
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& $exe @arguments 2>&1); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    $output | ForEach-Object { Log "  $_" }
    if ($code -ne 0) { throw "$step 실패 (exit=$code)" }
    return $output
}
function Read-RemoteSize([string]$path, [string]$step) {
    $output = Run-Capture 'ssh' ($SshArgs + @($SshHost,"stat -c %s '$path'")) $step
    $text = [string]($output | Select-Object -Last 1)
    [long]$size = 0
    if (![long]::TryParse($text.Trim(), [ref]$size)) { throw "$step 실패: 원격 크기를 숫자로 읽지 못했습니다: $text" }
    return $size
}
function Prune-WebDb {
    $root = [IO.Path]::GetFullPath($WebDbDir).TrimEnd('\') + '\'
    Get-ChildItem -LiteralPath $WebDbDir -File -Filter 'sabermetrics_v2_*.db' |
        Where-Object { $_.Name -match '^sabermetrics_v2_\d{8}_\d{6}(?:_\d{3})?\.db$' } |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip $KeepLocalWebDbCount |
        ForEach-Object {
            $target = [IO.Path]::GetFullPath($_.FullName)
            if (!$target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw 'DB 정리 경로 오류' }
            Remove-Item -LiteralPath $target -Force
        }
}
try {
    if ($SshExtraArgs) { $SshArgs += $SshExtraArgs -split '\s+' }
    if (!$LocalOnly -and !$FullDatabaseUpload) {
        $healthUri = $null
        if (![Uri]::TryCreate($RenderHealthUrl,[UriKind]::Absolute,[ref]$healthUri) -or $healthUri.Scheme -ne 'https') {
            throw 'Render health URL은 유효한 HTTPS 주소여야 합니다.'
        }
        Log "Render 내부 수집기 상태 확인: $RenderHealthUrl"
        try {
            $health = Invoke-RestMethod -Method Get -Uri $RenderHealthUrl -TimeoutSec 20
            if ($health.collector.enabled) {
                if ($health.collector.healthy) {
                    Log "Render 내부 수집기가 정상입니다. 마지막 성공=$($health.collector.lastSuccessfulCycleUtc); Windows 수집·DB 업로드·재시작을 건너뜁니다."
                    exit 0
                }
                throw "Render 내부 수집기가 등록됐지만 최근 성공 기록이 없습니다. 두 writer의 동시 실행을 막기 위해 이번 Windows 작업을 중단합니다. 수동 전체 복구만 -FullDatabaseUpload를 사용하세요."
            }
            Log '배포된 웹에 Render 내부 수집기가 없습니다. 기존 Windows 파이프라인을 복구 경로로 실행합니다.'
        }
        catch {
            Log "Render health 확인 실패 또는 내부 수집기 비정상: $($_.Exception.Message)"
            Log '두 writer의 동시 실행을 막기 위해 이번 작업을 중단합니다. 10분 뒤 다시 확인합니다.'
            exit 1
        }
    }
    foreach ($exe in @($RelayUIExe,$ImportCliExe,$WebAppExe)) {
        if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "실행 파일 없음. 먼저 Build-Automation.ps1 실행: $exe" }
    }
    $now = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Korea Standard Time')
    $from = $now.AddDays(-$LookbackDays).ToString('yyyy-MM-dd')
    $to = $now.ToString('yyyy-MM-dd')
    $collectLog = Join-Path $LogDir "collect_$stamp.log"
    Run $RelayUIExe @('--collect','--from',$from,'--to',$to,'--output',$CollectOutputDir,'--log',$collectLog) '1/7 진행 중 포함 JSON 수집'
    Run $ImportCliExe @('--import',$CollectOutputDir,$DesktopDb) '2/7 RESULT/ENDED 종료 경기 DB 반영'
    if (!(Test-Path -LiteralPath $DesktopDb)) { Log 'DB로 반영할 경기가 없습니다.'; exit 0 }
    if ($RunSeasonReconcile) {
        $reconcile = Join-Path $LogDir "reconcile_$stamp.json"
        Run $ImportCliExe @('--reconcile',$DesktopDb,$reconcile) '3/7 PC 공식 정정·타점·팀 자책점 대조'
    } else {
        Log '3/7 PC 공식 정정·타점·팀 자책점 대조 임시 건너뜀'
    }
    $verify = Join-Path $LogDir "verify_$stamp.json"
    $verifyArgs = @('--verify',$DesktopDb,$verify)
    if (!$RunSeasonReconcile) { $verifyArgs += '--structural-only' }
    Run $ImportCliExe $verifyArgs '4/7 DB 무결성·외래키·기록 오류 검증'
    $verified = Get-Content -LiteralPath $verify -Raw | ConvertFrom-Json
    if (!$verified.passed -or $null -eq $verified.dataVersion) { throw '검증 결과/DB 버전 누락' }
    $statePath = Join-Path $LogDir 'published-state.json'
    $identity = "$([IO.Path]::GetFullPath($DesktopDb))|$($verified.dataVersion)|$SshHost|$RemoteDbPath|$RenderServiceId"
    if (!$LocalOnly -and (Test-Path -LiteralPath $statePath)) {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if ($state.identity -eq $identity) { Log '현재 DB 버전은 이미 배포·재시작 요청 완료됐습니다.'; exit 0 }
    }
    if (!$LocalOnly -and !$FullDatabaseUpload) {
        if ([string]::IsNullOrWhiteSpace($RenderApiKey)) { $RenderApiKey = [Environment]::GetEnvironmentVariable('RENDER_API_KEY','User') }
        if ([string]::IsNullOrWhiteSpace($RenderApiKey)) { throw 'RENDER_API_KEY 없음. 원격 증분 반영 전에 중단합니다.' }
        foreach ($remotePath in @($RemoteDbPath,$RemoteToolRoot,$RemoteInboxRoot)) {
            if ($remotePath -notmatch '^/[A-Za-z0-9_./-]+$' -or $remotePath.Contains('..')) { throw "원격 경로 형식 오류: $remotePath" }
        }
        foreach ($localPath in @($RemoteToolLocalPath,$RemoteScriptLocalPath)) {
            if (!(Test-Path -LiteralPath $localPath -PathType Leaf)) { throw "원격 증분 도구 없음. Build-Automation.ps1 실행 필요: $localPath" }
        }

        $fromKey = $now.AddDays(-$LookbackDays).ToString('yyyyMMdd')
        $toKey = $now.ToString('yyyyMMdd')
        $candidateFiles = Get-ChildItem -LiteralPath $CollectOutputDir -File -Filter '*.json' | Where-Object {
            $_.BaseName -match '^(\d{8})' -and $Matches[1] -ge $fromKey -and $Matches[1] -le $toKey
        }
        $completeFiles = @($candidateFiles | Where-Object {
            try {
                $document = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                $statusCode = [string]$document.naver.result.game.statusCode
                $document.collectionStatus -eq 'complete' -and $statusCode.ToUpperInvariant() -in @('RESULT','ENDED')
            } catch { throw "수집 JSON 확인 실패: $($_.FullName): $($_.Exception.Message)" }
        })
        if ($completeFiles.Count -eq 0) { Log '5/7 원격에 반영할 RESULT/ENDED 완료 경기 JSON이 없습니다.'; exit 0 }

        $toolHash = (Get-FileHash -LiteralPath $RemoteToolLocalPath -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0,16)
        $scriptHash = (Get-FileHash -LiteralPath $RemoteScriptLocalPath -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0,16)
        $remoteTool = "$RemoteToolRoot/NaverRelay.Cli-$toolHash"
        $remoteScript = "$RemoteToolRoot/Remote-Incremental-$scriptHash.sh"
        $remoteInbox = "$RemoteInboxRoot/$stamp"
        Run 'ssh' ($SshArgs + @($SshHost,"mkdir -p '$RemoteToolRoot' '$remoteInbox'")) '5/7 원격 증분 작업 디렉터리 준비'
        $toolState = Run-Capture 'ssh' ($SshArgs + @($SshHost,"if [ -x '$remoteTool' ] && [ -x '$remoteScript' ]; then echo READY; else echo MISSING; fi")) '5/7 원격 증분 도구 확인'
        if ([string]($toolState | Select-Object -Last 1) -ne 'READY') {
            $remoteToolTemp = "$remoteTool.uploading-$stamp"
            $remoteScriptTemp = "$remoteScript.uploading-$stamp"
            Run 'scp' ($SshArgs + @('-s',$RemoteToolLocalPath,"${SshHost}:$remoteToolTemp")) '5/7 Linux 임포터 최초 업로드'
            if ((Read-RemoteSize $remoteToolTemp '5/7 Linux 임포터 크기 확인') -ne (Get-Item -LiteralPath $RemoteToolLocalPath).Length) {
                throw '원격 Linux 임포터 크기가 로컬과 다릅니다.'
            }
            Run 'scp' ($SshArgs + @('-s',$RemoteScriptLocalPath,"${SshHost}:$remoteScriptTemp")) '5/7 원격 증분 스크립트 업로드'
            if ((Read-RemoteSize $remoteScriptTemp '5/7 원격 스크립트 크기 확인') -ne (Get-Item -LiteralPath $RemoteScriptLocalPath).Length) {
                throw '원격 증분 스크립트 크기가 로컬과 다릅니다.'
            }
            Run 'ssh' ($SshArgs + @($SshHost,"chmod 755 '$remoteToolTemp' '$remoteScriptTemp' && mv -f '$remoteToolTemp' '$remoteTool' && mv -f '$remoteScriptTemp' '$remoteScript'")) '5/7 원격 증분 도구 설치'
        }

        $jsonArguments = $SshArgs + @('-s') + @($completeFiles.FullName) + @("${SshHost}:$remoteInbox/")
        Run 'scp' $jsonArguments "6/7 RESULT/ENDED 완료 경기 JSON $($completeFiles.Count)개 업로드"
        $remoteOutput = Run-Capture 'ssh' ($SshArgs + @($SshHost,"'$remoteScript' '$remoteTool' '$RemoteDbPath' '$remoteInbox' '$stamp'")) '6/7 Render 내부 임시 DB 증분 반영·검증·교체'
        $result = [string]($remoteOutput | Where-Object { "$_" -like 'REMOTE_RESULT=*' } | Select-Object -Last 1)
        if ($result -notmatch '^REMOTE_RESULT=(UPDATED|NO_CHANGE)\b') { throw "원격 증분 결과 누락: $result" }

        Log "7/7 Render 재시작 요청 ($result)"
        Invoke-RestMethod -Method Post -Uri "https://api.render.com/v1/services/$RenderServiceId/restart" -Headers @{ Authorization="Bearer $RenderApiKey" } -TimeoutSec 30 | Out-Null
        $stateTemp = "$statePath.$stamp.tmp"
        @{identity=$identity; mode='remote-incremental'; remoteResult=$result; requestedRestartAt=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $stateTemp -Encoding UTF8
        Move-Item -LiteralPath $stateTemp -Destination $statePath -Force
        Log '원격 증분 배포 및 재시작 요청 완료'
        exit 0
    }
    $webDb = Join-Path $WebDbDir "sabermetrics_v2_$stamp.db"
    Run $WebAppExe @('--prepare',$DesktopDb,$webDb) '5/7 검증된 웹 DB 봉인'
    if ($LocalOnly) { Prune-WebDb; Log "로컬 처리 완료: $webDb"; exit 0 }
    if ([string]::IsNullOrWhiteSpace($RenderApiKey)) { $RenderApiKey = [Environment]::GetEnvironmentVariable('RENDER_API_KEY','User') }
    if ([string]::IsNullOrWhiteSpace($RenderApiKey)) { throw 'RENDER_API_KEY 없음. 업로드 전에 중단합니다. 로컬 검증은 -LocalOnly 사용.' }
    if ($RemoteDbPath -notmatch '^/[A-Za-z0-9_./-]+$' -or $RemoteDbPath.Contains('..')) { throw '원격 DB 경로 형식 오류' }
    $remoteTemp = "$RemoteDbPath.$stamp.new"
    $localSize = (Get-Item -LiteralPath $webDb).Length
    Log "6/7 임시 DB 업로드 시작: $localSize bytes -> $remoteTemp"
    # Render 권장 방식대로 -s를 명시해 SFTP 프로토콜로 전송한다.
    Run 'scp' ($SshArgs + @('-s',$webDb,"${SshHost}:$remoteTemp")) '6/7 임시 DB 전송'
    $uploadedSize = Read-RemoteSize $remoteTemp '6/7 원격 임시 DB 크기 확인'
    if ($uploadedSize -ne $localSize) { throw "원격 임시 DB 크기 불일치: local=$localSize, remote=$uploadedSize. 원격 DB를 교체하지 않습니다." }
    Log "6/7 업로드 검증 완료: $uploadedSize bytes"
    $movedOutput = Run-Capture 'ssh' ($SshArgs + @($SshHost,"mv -f '$remoteTemp' '$RemoteDbPath' && sync && stat -c %s '$RemoteDbPath'")) '6/7 원격 DB 교체 및 크기 확인'
    $movedText = [string]($movedOutput | Select-Object -Last 1)
    [long]$movedSize = 0
    if (![long]::TryParse($movedText.Trim(), [ref]$movedSize) -or $movedSize -ne $localSize) {
        throw "교체된 원격 DB 크기 불일치: local=$localSize, remote=$movedText. Render를 재시작하지 않습니다."
    }
    Log '7/7 Render 재시작 요청'
    Invoke-RestMethod -Method Post -Uri "https://api.render.com/v1/services/$RenderServiceId/restart" -Headers @{ Authorization="Bearer $RenderApiKey" } -TimeoutSec 30 | Out-Null
    # Do not mark a failed deployment as done, even if the next import finds no changes.
    $stateTemp = "$statePath.$stamp.tmp"
    @{identity=$identity; requestedRestartAt=[DateTimeOffset]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content -LiteralPath $stateTemp -Encoding UTF8
    Move-Item -LiteralPath $stateTemp -Destination $statePath -Force
    Prune-WebDb
    Log '배포 및 재시작 요청 완료'
    exit 0
}
catch { Log "실패: $($_.Exception.Message)"; exit 1 }
finally { $lock.Dispose() }
