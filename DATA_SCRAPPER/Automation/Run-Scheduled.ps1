param(
    [string]$PipelinePath = "$PSScriptRoot\Run-Pipeline.ps1",
    [ValidateRange(0,540)][int]$KeepConsoleSeconds = 180,
    [string]$LogDir = "$env:USERPROFILE\Documents\NaverKboCombined\logs"
)

$ErrorActionPreference = 'Continue'
try {
    [Console]::OutputEncoding = [Text.Encoding]::UTF8
    $OutputEncoding = [Text.Encoding]::UTF8
} catch {}

if (!(Test-Path -LiteralPath $PipelinePath -PathType Leaf)) {
    throw "파이프라인 스크립트가 없습니다: $PipelinePath"
}

Write-Host "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] KBO 자동화 시작"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PipelinePath
$pipelineExitCode = $LASTEXITCODE

$latestLog = Get-ChildItem -LiteralPath $LogDir -File -Filter 'pipeline_*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Write-Host ''
Write-Host "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] 파이프라인 종료 코드: $pipelineExitCode"
if ($latestLog) { Write-Host "로그: $($latestLog.FullName)" }
if ($KeepConsoleSeconds -gt 0) {
    Write-Host "이 창은 로그 확인을 위해 ${KeepConsoleSeconds}초 뒤 닫힙니다. 계속 보려면 Watch-Logs.ps1을 실행하세요."
    Start-Sleep -Seconds $KeepConsoleSeconds
}
exit $pipelineExitCode
