<#
.SYNOPSIS
  Run-Pipeline.ps1을 저녁 시간대(기본 18:00~00:30)에 20분 간격으로 반복 실행하는
  Windows 작업 스케줄러 작업을 등록합니다. 한 번만 실행하면 됩니다.

.NOTES
  Register-ScheduledTask(PowerShell cmdlet) 대신 schtasks.exe를 씁니다 -
  New-ScheduledTaskTrigger -Daily가 반환하는 트리거는 PowerShell/OS 버전에
  따라 .Repetition 속성이 null로 나와서 반복 간격 설정이 조용히 실패하는
  경우가 있는데, schtasks.exe는 이 문제 없이 안정적으로 등록됩니다.

  기본적으로 "로그온한 사용자로만 실행"으로 등록됩니다(비밀번호 저장 불필요).
  로그아웃 상태에서도 돌리고 싶다면 작업 스케줄러(taskschd.msc)에서 이 작업을
  열어 "사용자가 로그온했는지 여부에 관계없이 실행"으로 직접 바꾸고, 계정
  비밀번호를 그때 입력해 주세요 (이 스크립트는 비밀번호를 다루지 않습니다).
#>

param(
    [string]$TaskName = "KBO_Sabermetrics_AutoSync",
    [string]$ScriptPath = "$PSScriptRoot\Run-Pipeline.ps1",
    [string]$StartTime = "18:00",
    [int]$IntervalMinutes = 20,
    # HH:MM 형식
    [string]$Duration = "06:30"
)

$exec = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`""

# /f: 이미 같은 이름의 작업이 있으면 확인 없이 덮어씀
& schtasks /create /tn $TaskName /tr $exec /sc daily /st $StartTime /ri $IntervalMinutes /du $Duration /f

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "등록 완료: '$TaskName' ($StartTime 부터 ${IntervalMinutes}분 간격으로 $Duration 동안 반복)"
    Write-Host "지금 바로 한 번 테스트하려면: schtasks /run /tn `"$TaskName`""
    Write-Host "등록 내용 확인하려면: schtasks /query /tn `"$TaskName`" /v /fo list"
} else {
    Write-Host "등록 실패 (종료 코드 $LASTEXITCODE) - 위 schtasks 출력의 에러 메시지를 확인해주세요."
}
