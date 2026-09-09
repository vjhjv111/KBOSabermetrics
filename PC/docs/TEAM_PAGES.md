# 팀 페이지와 팀 단위 분석

## 목적

선수 개인 페이지에서 제공하던 연도별 기록, Value·WAR, 상대별·상황별·구종별 분석을 팀 단위로 확장합니다. 팀 페이지는 별도 WinForms 창으로 열리며, 향후 ASP.NET Core/React에서도 동일한 `TeamPageData` 계약을 사용할 수 있습니다.

## 열기

```text
메인 도구모음 → 팀 페이지
Ctrl+T
현재 팀 필터가 선택돼 있으면 해당 팀을 바로 열기
전체 팀이면 팀 선택 창 표시
```

팀 목록과 팀 페이지는 다음 조건만 사용합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode,''))) = 'kbo_r'
```

따라서 올스타전의 `WO`, `WE` 같은 임시 팀 코드는 정규 구단 팀 페이지와 소속 이력에 포함되지 않습니다.

## 화면 구성

### 연도별 타격

팀의 모든 `BatterGameStats` 행을 시즌별로 합산한 뒤 AVG, OBP, SLG, OPS, ISO, BABIP, wOBA, wRAA, wRC, wRC+, OPS+를 다시 계산합니다. 선수별 비율을 단순 평균하지 않습니다.

### 연도별 투구

`PitcherGameStats`의 원본 경기별 최종 투수 라인을 합산합니다. IP, R, ER, H, HR, BB, HBP, SO, IFFB, ERA, WHIP, FIP, xFIP, ifFIP, FIPR9, 파크 팩터, pFIPR9, dRPW, gmLI와 팀 투수 WAR 합계를 표시합니다.

### Value·WAR

팀 소속 선수들의 타격·주루·수비·포지션·대체선수 Runs와 타자/투수 WAR를 시즌별로 합산합니다. 현재 수비 추적 데이터의 한계는 기존 선수 WAR 정책과 같습니다.

### 선수별 타격·투구

해당 팀에서 기록한 시즌별 선수 성적을 표시합니다. 행을 더블클릭하면 pcode 기준 선수 개인 페이지가 열립니다.

### 상대전적

상대 팀별로 다음 항목을 표시합니다.

```text
G / W / L / D / 승률
득점 / 실점 / 득실차
PA / AB / H / HR / BB / SO / AVG / OBP / SLG / OPS
IP / ER / ERA / FIP
```

상대 팀 행을 더블클릭하면 그 팀의 팀 페이지를 새 창으로 엽니다.

### 상황별

타격과 투구 관점을 함께 제공하며 다음 축을 선택할 수 있습니다.

```text
주자
아웃
이닝
점수차
득점권
클러치
장소
선두타자
2아웃
```

현재 클러치 기준은 `7회 이후 AND 절대 점수차 3점 이내`입니다.

### 구종별

#### 팀 타격

각 타석의 마지막 실제 투구 구종에 타석 결과를 귀속합니다.

```text
구종 / PA / AB / H / 1B / 2B / 3B / HR
BB / HBP / SO / AVG / OBP / SLG / OPS / wOBA
```

볼넷과 사구도 마지막 투구 구종 기준입니다. 따라서 표에는 `최종구 기준`임을 명시합니다.

#### 팀 투구

팀 투수의 실제 투구를 구종별로 집계합니다.

```text
투구 수 / 사용률 / 평균 구속 / 스트라이크%
Swing% / Whiff% / Contact% / CSW%
상대 PA / 상대 AB / 피안타 / 피홈런 / 피AVG / 피OPS
```

## 데이터 접근

팀 페이지는 다음 관계형 테이블만 읽습니다.

```text
Games
BatterGameStats
PitcherGameStats
PlateAppearances
Pitches
```

원본 JSON이나 정규화 JSON blob을 다시 열지 않습니다. 팀 조회를 위해 다음 인덱스를 사용합니다.

```text
IX_Games_TeamSeasonDate
IX_PlateAppearances_BattingTeamGame
IX_PlateAppearances_FieldingTeamGame
IX_Pitches_TypeResult
```

## 웹 API

```http
GET /api/teams
GET /api/teams/{teamCode}
```

응답 DTO는 `NaverRelay.Application.Teams`에 있으며 WinForms에 의존하지 않습니다. PostgreSQL로 이전할 때도 `ITeamPageService`와 DTO를 유지하고 Infrastructure 구현만 교체할 수 있습니다.

## 현재 제한

- 상대전적은 현재 상대 팀 기준입니다. 상대 선수별 분석은 선수 페이지에서 제공하며 이후 팀 페이지에도 추가할 수 있습니다.
- 구종별 타격은 최종 투구 귀속 방식이며 타석 내 모든 구종의 독립적인 타격 가치를 의미하지 않습니다.
- 팀 WAR는 현재 프로젝트의 Site WAR/FanGraphs형 WAR 정책을 선수별로 합산한 값입니다.
- 팀명 이력은 같은 네이버 팀 코드의 정규시즌 명칭 변화를 시간순으로 표시합니다.
