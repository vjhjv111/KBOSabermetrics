<#
.SYNOPSIS
  KBO 경기 자동 수집 -> 데스크톱 DB 반영 -> 웹용 DB 봉인 -> Render 안전 업로드
  -> Render 웹 서비스 재시작, 다섯 단계를 한 번에 실행합니다. Windows 작업
  스케줄러(Task Scheduler)에서 저녁 시간대에 주기적으로 실행하도록 등록해서 씁니다.

.NOTES
  - 각 단계는 재실행에 안전합니다(이미 완료된 경기/변경 없는 파일/이미 있는
    출력 파일은 각 도구가 알아서 건너뜁니다). 도중에 실패해도 다음 실행에서
    이어서 처리됩니다.
  - 아직 진행 중인 경기(완료 안 됨)가 있는 건 정상 상태입니다. 이번 주기에
    새로 완료된 경기가 하나도 없으면 3/5~5/5단계(봉인/업로드/재시작)는
    건너뛰고 조용히 끝냅니다 - Render로 불필요한 업로드/재시작을 반복하지
    않기 위해서입니다.
  - Render의 서비스 중인 DB 파일을 직접 덮어쓰지 않습니다. 임시 파일명으로
    올린 뒤, 서버에서 원자적 rename으로 교체합니다(전송 중 잘린 파일을
    서비스가 읽는 상황을 방지).
  - 5/5 단계(재시작)가 필요한 이유: 웹 서버가 SQLite 연결 풀링(Pooling=true)을
    쓰고 있어서, rename으로 파일을 교체해도 이미 그 파일을 열어놓은 채
    떠 있는 프로세스는 계속 예전 내용을 붙잡고 읽습니다. Render 서비스를
    재시작해야 새 DB 파일을 다시 열어서 최신 데이터를 반영합니다.
  - 5/5 단계를 쓰려면 Render API 키가 필요합니다(최초 1회):
      1) https://dashboard.render.com -> 우측 상단 계정 아이콘 -> Account
         Settings -> API Keys -> Create API Key
      2) 만든 키를 PowerShell에서 한 번만 등록:
           setx RENDER_API_KEY "여기에_발급받은_키_붙여넣기"
         등록 후 PowerShell 창을 껐다 켜야 반영됩니다(작업 스케줄러로 실행되는
         것도 다음 로그온/새 프로세스부터 자동으로 이 값을 읽습니다).
      RENDER_API_KEY가 설정되어 있지 않으면 5/5 단계는 경고만 남기고
      건너뜁니다 - 이 경우 DB 파일 자체는 이미 정상 교체되어 있으니, Render
      대시보드에서 수동으로 한 번 Restart 눌러주면 됩니다.
  - 사전 준비(최초 1회):
      dotnet build "<NaverKboRelayUI 경로>\NaverKboRelayUI.csproj" -c Release
      dotnet build "<KBOSabermetrics 경로>\PC\src\NaverRelay.Cli\NaverRelay.Cli.csproj" -c Release
      dotnet build "<KBOSabermetrics 경로>\WEB\server\src\NaverSabermetrics.Web\NaverSabermetrics.Web.csproj" -c Release
    위 세 개를 먼저 한 번 빌드해서 bin\Release 폴더가 만들어져 있어야 합니다.
#>

param(
    [string]$CollectOutputDir = "$env:USERPROFILE\Documents\NaverKboCombined",
    [string]$DesktopDb        = "$env:LOCALAPPDATA\NaverSabermetrics\Data\sabermetrics_v2.db",
    [string]$WebDbDir         = "$env:USERPROFILE\Documents\NaverKboCombined\web-db",
    [string]$LogDir           = "$env:USERPROFILE\Documents\NaverKboCombined\logs",

    [string]$RelayUIExe   = "C:\Users\vjhjv\Documents\Codex\2026-09-12\https-www-koreabaseball-com-game-livetext\outputs\NaverKboRelayUI-Source\NaverKboRelayUI\bin\Release\net8.0-windows\NaverKboRelayUI.exe",
    [string]$ImportCliExe = "C:\repository\KBO_sabermetrics\KBOSabermetrics\KBOSabermetrics\PC\src\NaverRelay.Cli\bin\Release\net8.0\NaverRelay.Cli.exe",
    [string]$WebAppExe    = "C:\repository\KBO_sabermetrics\KBOSabermetrics\KBOSabermetrics\WEB\server\src\NaverSabermetrics.Web\bin\Release\net8.0\NaverSabermetrics.Web.exe",

    # scp/ssh에 -i 키 옵션이 필요하면 예: "-i C:\Users\vjhjv\.ssh\id_ed25519"
    [string]$SshExtraArgs = "",
    [string]$SshHost      = "srv-dah4arpt0dsc73egvl3g@ssh.singapore.render.com",
    [string]$RemoteDbPath = "/var/data/sabermetrics_v2.db",

    # Render REST API로 웹 서비스를 재시작하기 위한 정보.
    # RenderServiceId 기본값은 위 SshHost의 "srv-..." 부분과 동일합니다.
    # RenderApiKey는 기본적으로 환경변수 RENDER_API_KEY에서 읽습니다
    # (설정 방법은 위 .NOTES 참고). 비어 있으면 5/5 단계는 건너뜁니다.
    [string]$RenderServiceId = "srv-dah4arpt0dsc73egvl3g",
    [string]$RenderApiKey    = $env:RENDER_API_KEY,

    # 로컬 web-db 폴더에 남겨둘 최근 봉인 파일 개수
    [int]$KeepLocalWebDbCount = 3
)

$ErrorActionPreference = "Stop"

# 콘솔/자식 프로세스 출력이 한글 깨짐 없이 보이도록 UTF-8로 고정.
try {
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
    $OutputEncoding = [System.Text.Encoding]::UTF8
} catch {}

New-Item -ItemType Directory -Force -Path $CollectOutputDir, $WebDbDir, $LogDir | Out-Null

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $LogDir "pipeline_$stamp.log"
$lockFile = Join-Path $LogDir "pipeline.lock"

function Log([string]$msg) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
    Write-Host $line
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Invoke-GuiExe([string]$exe, [string[]]$exeArgs, [string]$stepName) {
    # WinExe(창 서브시스템) 프로그램 실행용 - 콘솔에 직접 출력하지 않으므로
    # --log 파일로 로그를 받습니다.
    Log "  실행: `"$exe`" $($exeArgs -join ' ')"
    $proc = Start-Process -FilePath $exe -ArgumentList $exeArgs -Wait -NoNewWindow -PassThru
    if ($proc.ExitCode -ne 0) {
        throw "$stepName 실패 (종료 코드 $($proc.ExitCode))"
    }
}

function Invoke-ConsoleExe([string]$exe, [string[]]$exeArgs, [string]$stepName) {
    # 콘솔 프로그램 실행용 - stdout/stderr를 그대로 받아서 로그에 남기고 반환합니다.
    #
    # 주의: 네이티브 프로그램의 stderr 한 줄이라도 나오면(scp/ssh는 진행 상황을
    # stderr로 출력하는 게 정상입니다), 2>&1로 합칠 때 Windows PowerShell은 그
    # 줄을 ErrorRecord로 만듭니다. 전역 $ErrorActionPreference="Stop"이 걸린
    # 상태에서 이걸 만나면 실제 실패가 아닌데도 스크립트가 그 줄에서 바로
    # 중단돼 버리므로, 이 함수 안에서는 일시적으로 Continue로 풀어주고
    # 종료 코드($LASTEXITCODE)만으로 성공/실패를 판단합니다.
    Log "  실행: `"$exe`" $($exeArgs -join ' ')"
    $previousPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $exe @exeArgs 2>&1 | ForEach-Object { "$_" }
    } finally {
        $ErrorActionPreference = $previousPref
    }
    $output | ForEach-Object { Log "    $_" }
    if ($LASTEXITCODE -ne 0) {
        throw "$stepName 실패 (종료 코드 $LASTEXITCODE)"
    }
    return $output
}

# 같은 파이프라인이 겹쳐 실행되는 것 방지 (이전 실행이 아직 진행 중이면 이번 트리거는 건너뜀).
# 90분 넘게 남아있는 lock은 비정상 종료로 보고 무시합니다.
if (Test-Path $lockFile) {
    $age = (Get-Date) - (Get-Item $lockFile).LastWriteTime
    if ($age.TotalMinutes -lt 90) {
        Write-Host "이전 실행이 아직 진행 중인 것으로 보여 이번 실행은 건너뜁니다. ($lockFile)"
        exit 0
    }
    Log "오래된 lock 파일을 무시하고 진행합니다 (age=$($age.TotalMinutes)분)."
}
New-Item -ItemType File -Force -Path $lockFile | Out-Null

try {
    Log "===== 파이프라인 시작 ====="

    # 1) 오늘 경기 수집 (완료된 경기만 저장됨; 이미 완료 저장된 파일은 자동으로 건너뜀)
    Log "1/5 경기 수집"
    $collectLog = Join-Path $LogDir "collect_$stamp.log"
    Invoke-GuiExe $RelayUIExe @("--collect", "--output", $CollectOutputDir, "--log", $collectLog) "경기 수집"

    # 2) 새로 완료된 JSON을 데스크톱 DB에 반영 (변경 없는 파일/아직 진행 중인 경기는 자동으로 건너뜀)
    Log "2/5 데스크톱 DB 반영"
    $importOutput = Invoke-ConsoleExe $ImportCliExe @("--import", $CollectOutputDir, $DesktopDb) "DB 반영"

    $importedCount = 0
    foreach ($line in $importOutput) {
        if ($line -match 'imported=(\d+)') { $importedCount = [int]$Matches[1] }
    }

    if ($importedCount -eq 0) {
        Log "새로 완료되어 반영된 경기가 없어 3/5~5/5(봉인/업로드/재시작)는 건너뜁니다. 정상 상태입니다."
        Log "===== 파이프라인 종료 (변경 없음) ====="
        exit 0
    }
    Log "새로 반영된 경기 $importedCount 건. 봉인/업로드를 진행합니다."

    # 3) 데스크톱 DB -> 새 웹용 DB 봉인 (매번 새 파일이어야 함 - 기존 파일 덮어쓰기 불가)
    Log "3/5 웹 DB 봉인"
    $webDb = Join-Path $WebDbDir "sabermetrics_v2_$stamp.db"
    Invoke-ConsoleExe $WebAppExe @("--prepare", $DesktopDb, $webDb) "웹 DB 봉인" | Out-Null

    # 4) Render에 임시 파일명으로 업로드 후, 서버에서 원자적으로 교체
    #    (서비스 중인 파일에 바로 덮어쓰면 전송 도중 반쯤 써진 파일을 읽을 위험이 있어
    #     반드시 임시 경로에 올린 뒤 rename으로 바꿔치기합니다)
    Log "4/5 Render 업로드"
    $remoteTemp = "$RemoteDbPath.new"
    $sshArgList = @()
    if ($SshExtraArgs) { $sshArgList += $SshExtraArgs -split '\s+' }

    Invoke-ConsoleExe "scp" ($sshArgList + @("-s", $webDb, "${SshHost}:${remoteTemp}")) "scp 업로드" | Out-Null
    Invoke-ConsoleExe "ssh" ($sshArgList + @($SshHost, "mv -f `"$remoteTemp`" `"$RemoteDbPath`"")) "원격 rename" | Out-Null

    Log "===== 파일 교체 성공 ($webDb) ====="

    # 5) Render 웹 서비스 재시작
    #    파일을 rename으로 바꿔치기해도, 이미 그 DB 파일을 열어놓고 떠 있는 웹
    #    서비스 프로세스는 SQLite 연결 풀링 때문에 계속 예전 내용을 붙잡고
    #    읽습니다. 재시작해야 새 파일을 다시 열어서 최신 데이터가 반영됩니다.
    Log "5/5 Render 서비스 재시작"
    if ([string]::IsNullOrWhiteSpace($RenderApiKey)) {
        Log "  RENDER_API_KEY가 설정되어 있지 않아 재시작을 건너뜁니다."
        Log "  DB 파일 자체는 이미 정상 교체됐으니, Render 대시보드에서 해당 서비스를 수동으로 한 번 Restart 해주세요."
        Log "  다음부터 자동으로 재시작하려면 PowerShell에서: setx RENDER_API_KEY `"발급받은_키`"  (자세한 방법은 스크립트 상단 .NOTES 참고)"
    } else {
        try {
            $headers = @{ Authorization = "Bearer $RenderApiKey" }
            $restartUrl = "https://api.render.com/v1/services/$RenderServiceId/restart"
            Invoke-RestMethod -Method Post -Uri $restartUrl -Headers $headers -TimeoutSec 30 | Out-Null
            Log "  재시작 요청 완료. Render에서 새 서비스가 다시 뜨는 데 1~2분 정도 걸릴 수 있습니다."
        } catch {
            Log "  재시작 요청 실패 (DB 파일 자체는 이미 정상적으로 교체되어 있습니다): $($_.Exception.Message)"
            Log "  Render 대시보드에서 해당 서비스를 수동으로 한 번 Restart 해주세요."
        }
    }

    Log "===== 파이프라인 성공 ====="

    # 로컬 web-db 폴더 정리 - 최근 N개만 보관
    Get-ChildItem $WebDbDir -Filter "sabermetrics_v2_*.db" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $KeepLocalWebDbCount |
        Remove-Item -Force -ErrorAction SilentlyContinue

    exit 0
}
catch {
    Log "!!! 파이프라인 실패: $($_.Exception.Message)"
    exit 1
}
finally {
    Remove-Item -Path $lockFile -Force -ErrorAction SilentlyContinue
}
