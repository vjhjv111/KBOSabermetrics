# FanGraphs형 투수 WAR v2

> 현재 계산 엔진은 `KBO_PITCHER_WAR_V3.md`의 KBO 역할별 대체수준·WARIP·RA9-WAR 확장을 사용합니다. 이 문서는 v2 구조 기록용입니다.


구현 기준

- 정규시즌: `roundCode == "kbo_r"`
- 투수 IP/R/ER/HR/BB/HBP/SO: 원본 경기별 최종 투수 기록
- IFFB: 포수 파울플라이, 1루수 파울플라이, 2루수 뜬공, 3루수 뜬공, 유격수 뜬공, 투수 뜬공
- 선발/구원: 경기별 투수 최종 기록 `seqno == 1`은 선발, 나머지는 구원
- gmLI: 원본 `metricOption.wpaByPlate` 유효값을 이용한 입장상황 근사치
- 파크 팩터: 로드된 모든 `kbo_r` JSON의 구장 내 FIP 구성요소율과 동일 팀 원정 경기율 비교
- 다년 JSON: 파일/폴더/ZIP을 함께 불러오면 파크 팩터 탭에서 전체 연도 자동 집계

주의

이 값은 FanGraphs의 계산 구조를 KBO 원본 데이터에 적용한 사이트용 구현입니다. FanGraphs가 공개하지 않은 내부 보정 및 공식 MLB 파크 팩터와 완전히 동일한 값은 아닙니다. 현재 리그 재중앙화 보정은 0으로 표시됩니다.
