# KBO Pitcher WAR diagnostics

이 기능은 현재 KBO Pitcher WAR v3 공식을 변경하지 않고, 실제 DB의 5개 시즌에서 대체선수 수준을 관찰하기 위한 진단 전용 기능입니다.

## 메뉴
연도별 상수 -> 투수 WAR 진단
연도별 상수 -> 대체후보

## 투수 WAR 진단에서 확인할 핵심 열
- Year / G / 리그 IP / lgERA / lgRA9 / lgFIPR9
- SP/RP 전체 역할표본 / 후보 수 / 후보 최대 IP / 후보 IP
- SP/RP 관측 FIP- 평균 / 회귀 FIP- 평균
- SP/RP P25 / P50 / P75 / P90
- SP/RP 3Y P75 / 5Y P75
- SP/RP FG 하한 FIP-
- SP/RP 진단 적용 FIP-
- SP/RP P75 RA9 / 진단 Repl RA9
- 목표 투수 WAR / 보정 전 fWAR / 달성률 / WARIP / 보정 후 fWAR
- 보정 전 RA9-WAR / 달성률 / RA9 WARIP / 보정 후 RA9-WAR
- 현재 v3 SP/RP Repl FIP-

`진단 적용 FIP-`는 현재 v3의 철학과 같은 방식으로, 해당 시즌의 회귀 평균 대체수준과 FanGraphs형 하한 중 더 나쁜 쪽을 사용한 연도별 진단값입니다. P75 값은 관찰용이며 아직 WAR에 적용하지 않습니다.

## 대체후보에서 확인할 핵심 열
- Year / SP 또는 RP
- 선수 코드 / 선수 / 팀
- G / IP / PF
- pFIPR9 / 관측 FIP- / 회귀 FIP-
- pRA9 / 회귀 RA9
- gmLI / 회귀 기준 IP

후보선수 명단을 보고 실제로 '1군 대체급'이라고 부를 만한 선수들이 들어왔는지 검토해야 합니다.

## CSV로 보내줄 파일
1. 연도별 상수 -> 투수 WAR 진단 -> CSV
2. 연도별 상수 -> 대체후보 -> CSV

이 두 CSV를 보내면 다음을 재검토할 수 있습니다.
- SP/RP 후보 선정 방식
- 최소/최대 IP
- 회귀 강도
- P70/P75/P80 등 적절한 percentile
- 1년/3년 rolling/5년 고정 기준
- KBO 고정 Replacement FIP-
- WARIP가 과도한지 여부
- .294 replacement winning percentage / 투수 WAR 43% 정책 재검토

## 계산 범위
진단은 정확히 roundCode = kbo_r 인 경기만 사용하며, 시즌별로 따로 리그 환경을 다시 계산합니다.
현재 WAR v3 공식 자체는 이 진단 기능 추가로 변경되지 않습니다.
