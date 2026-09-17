[CmdletBinding()]
param(
    [string]$SshTarget = "srv-dah4arpt0dsc73egvl3g@ssh.singapore.render.com",
    [string]$TriggerPath = "/var/data/kbo-render-collector.trigger"
)

$ErrorActionPreference = "Stop"
if ($SshTarget -notmatch '^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+$') {
    throw "SSH 대상 형식이 올바르지 않습니다."
}
if ($TriggerPath -notmatch '^/var/data/[A-Za-z0-9._-]+$') {
    throw "트리거 경로는 /var/data 아래의 단일 파일이어야 합니다."
}

& ssh -o BatchMode=yes -o ConnectTimeout=20 -o UpdateHostKeys=no $SshTarget "touch $TriggerPath"
if ($LASTEXITCODE -ne 0) { throw "Render SSH 수동 실행 요청 실패 (종료 코드 $LASTEXITCODE)" }
Write-Host "수동 실행 요청 완료: 수집기가 약 2초 안에 시작합니다."
