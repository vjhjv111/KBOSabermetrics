[CmdletBinding()]
param(
    [string]$BaseUrl = "https://kbosabermetrics.onrender.com",
    [string]$Key = $env:KBO_COLLECTOR_TRIGGER_KEY
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Key)) {
    throw "KBO_COLLECTOR_TRIGGER_KEY 환경 변수가 없습니다. 수동 실행 키를 먼저 등록하세요."
}

$uri = $BaseUrl.TrimEnd('/') + "/api/admin/collector/run"
$response = Invoke-RestMethod -Method Post -Uri $uri -Headers @{ Authorization = "Bearer $Key" }
$response | ConvertTo-Json -Depth 6
