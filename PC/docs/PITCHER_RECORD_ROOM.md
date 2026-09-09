# 투수 기록실 UI·통계 업데이트

버전: `2.5-pitcher-record-room`

첨부된 투수 기록실 화면의 탭 순서를 기준으로 다음 구성을 적용했습니다.

```text
기본 / 심화 / 가치 / 확장 / WP / 주자 / 선발 / 구원 / 타구 / 타구방향 / 투구 / 구종
```

## 구현된 탭

### 기본

경기 수, 선발·구원 등판, 이닝, 실점·자책점, 상대 타자, 피안타·피홈런,
볼넷·사구·탈삼진·내야뜬공·폭투, ERA·RA9·FIP·WHIP·WAR를 표시합니다.
승·패·세이브·홀드처럼 현재 원본 최종 투수 라인에서 안정적으로 확보하지 못하는
의사결정 기록은 표시하지 않습니다.

### 심화

K/9, BB/9, K/BB, HR/9, K%, BB%, K-BB%, BABIP, LOB%, ERA, RA9,
FIP, xFIP, FIP-, xFIP-, ERA-FIP, 피AVG·피OBP, NP, P/G, P/IP, P/PA를 표시합니다.

### 가치

선발·구원·종합 이닝, 동적 RPW, RAA, 대체선수 Runs, RAR, WAA, WAR를 역할별로 분리합니다.
연봉 관련 열은 원본에 없으므로 제외했습니다.

### 확장

TTO%, 구종 수, Pitch Entropy, 정규화 구종 다양성 지수와 구종별 사용률을 표시합니다.
스크린샷의 외부 독자 지표 중 공식 또는 원자료가 명확하지 않은 항목은 임의로 만들지 않았습니다.

### WP

원본 타석 WPA를 투수 관점으로 바꿔 pLI, gmLI, WPA+, WPA-, WPA, WPA/LI를 표시합니다.
`pLI`, `gmLI`, `WPA/LI`는 원본 승리확률·WPA를 이용한 사이트 추정치라 `*`로 표시합니다.

### 주자

도루 허용, 도루 저지, 성공률, 시도 수, 도루·도루실패 도착 루별 기록과 폭투를 표시합니다.
포수 전용 저지 책임이나 견제구 횟수처럼 현재 데이터에서 투수 책임을 정확히 분리할 수 없는 항목은 제외했습니다.

### 선발

GS, 선발 IP·ERA, QS·QS+, 득점 지원 추정치, 선발 경기 팀 승패,
IP/GS, P/GS, 선발 WAR를 표시합니다.
득점 지원은 해당 선발 등판 경기의 팀 최종 득점을 사용한 근사치이므로 `*`로 표시합니다.

### 구원

구원 등판, 구원 IP·ERA, 2·3·4일 연속 등판, 1이닝 이상 등판,
IP/GR, P/GR, gmLI, 구원 WAR를 표시합니다.

### 타구

BIP, BABIP, GB%, ifFB%, ofFB%, FB%, LD%, GB/FB, HR/FB%, 내야안타%를 표시합니다.

### 타구방향

좌·좌중·중앙·우중·우 방향 비율·개수·피안타·피AVG,
타자 손잡이를 반영한 당겨치기·밀어치기 비율과 BABIP를 표시합니다.

### 투구

스트라이크·루킹·헛스윙·CSW, Swing/Contact/Whiff,
초구 스트라이크·초구 헛스윙, Putaway, 존·존 밖·하트존 지표,
루킹·헛스윙 삼진 분포를 표시합니다.

### 구종

투심·포심·커터·커브·슬라이더·체인지업·싱커·포크·너클·기타별로 다음을 피벗해 표시합니다.

```text
WPA 기반 투구 가치*
100구당 가치*
평균 구속
구사율
투구 수
최종구 기준 피AVG
최종구 기준 피SLG
```

## 데이터 경계

모든 조회는 최초 import 후 관계형 SQLite의 다음 테이블만 사용합니다.

```text
Games / PitcherGameStats / PlateAppearances / Pitches / RunnerEvents / Players
```

원본 JSON이나 경기 전체 JSON blob을 다시 읽지 않습니다.

## 웹 API

```http
GET /api/record-room/pitchers/basic
GET /api/record-room/pitchers/advanced
GET /api/record-room/pitchers/value
GET /api/record-room/pitchers/extended
GET /api/record-room/pitchers/wp
GET /api/record-room/pitchers/runner
GET /api/record-room/pitchers/starter
GET /api/record-room/pitchers/reliever
GET /api/record-room/pitchers/batted-ball
GET /api/record-room/pitchers/direction
GET /api/record-room/pitchers/discipline
GET /api/record-room/pitchers/pitch-types
```

공통 쿼리 인자는 타자 기록실과 동일하게 `room`, `year`, `competition`, `team`,
`opponent`, `venue`, `stadium`, `startDate`, `endDate`를 사용합니다.
