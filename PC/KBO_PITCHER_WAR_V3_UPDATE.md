# KBO 투수 WAR v3 업데이트

이번 버전은 기존 FanGraphs형 ifFIP WAR 흐름 위에 KBO 데이터 기반 역할별 대체선수 수준, 리그 WARIP 재중앙화, RA9-WAR, Blend WAR를 추가합니다.

자세한 계산식과 해석 주의사항은 `docs/KBO_PITCHER_WAR_V3.md`를 확인하세요.

## 핵심 지표

- `KBO 선발 Repl FIP-`
- `KBO 구원 Repl FIP-`
- `보정 전 fWAR`
- `KBO fWAR WARIP`
- `WARIP 보정`
- `KBO fWAR v3`
- `KBO RA9-WAR`
- `Blend WAR 70/30`

## 기존 DB

기존 `sabermetrics_v2.db`를 그대로 사용합니다. JSON을 다시 적재할 필요는 없습니다. 계산 캐시 버전이 변경되어 리그 상수와 투수 통계가 최초 한 번 다시 계산됩니다.

## 공식표

GUI의 `공식·계산기`에서 다음 항목을 확인할 수 있습니다.

- KBO 투수 대체수준
- KBO WARIP
- KBO fWAR v3
- KBO RA9-WAR
- Blend WAR 70/30
