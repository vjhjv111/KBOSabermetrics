# Naver Sabermetrics V2 팀 페이지 구현·검증 보고서

## 구현 기준

기준 프로젝트: `NaverSabermetrics_V2_PlayerSplits_TeamHistoryFixed`

팀 페이지는 선수 개인 페이지와 동일한 관계형 SQLite 데이터 원칙을 사용합니다.

```text
원본 JSON은 신규·변경 경기 import에서만 사용
→ 팀 화면은 Games/BatterGameStats/PitcherGameStats/PlateAppearances/Pitches만 조회
```

팀 목록과 모든 팀 통계는 정확히 다음 정규시즌 조건만 포함합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode,''))) = 'kbo_r'
```

## 추가된 프로젝트 파일

```text
src/NaverRelay.Application/Teams/TeamPageContracts.cs
src/NaverRelay.Infrastructure.Sqlite/DatabaseTeamPageService.cs
src/NaverRelay.Infrastructure.Sqlite/DatabaseTeamPageService.Splits.cs
src/NaverRelay.Gui/TeamSelectionDialog.cs
src/NaverRelay.Gui/TeamDetailForm.cs
docs/TEAM_PAGES.md
validation/validate_team_pages.py
```

## 데스크톱 기능

### 팀 페이지 열기

- 메인 도구모음 `팀 페이지` 버튼
- `Ctrl+T`
- 메인 화면에 팀 필터가 선택돼 있으면 해당 팀을 바로 염
- 전체 팀 상태에서는 팀명·팀 코드 검색 선택창 표시

### 팀 상세 창

- 팀 코드, 최근 팀명, 팀명 이력, 활동 시즌, 사용 구장
- 정규시즌 통산 G/W/L/D/승률/득실차
- 표시 시즌 필터
- 모든 DataGridView 열 머리글 오름·내림차순 정렬

### 연도별 팀 타격

- G/W/L/D
- PA, AB, R, H, 1B, 2B, 3B, HR, TB, RBI
- SB, CS, BB, IBB, HBP, SO, GDP, SH, SF
- AVG, OBP, SLG, OPS, ISO, BABIP
- BB%, K%, wOBA, wRAA, wRC, wRC+, OPS+
- 해당 팀 타자 WAR 합

비율 통계는 선수별 수치를 평균하지 않고 시즌 팀 누적 분자·분모에서 다시 계산합니다.

### 연도별 팀 투구

- IP, R, ER, H, HR, BB, HBP, SO, IFFB
- ERA, WHIP, K/9, BB/9, HR/9
- FIP, xFIP, ifFIP, FIPR9
- FIP 파크 팩터, pFIPR9, 동적 dRPW, 불펜 gmLI
- 해당 팀 투수 WAR 합

### 팀 Value·WAR

- 타격 Runs
- 주루 Runs
- 수비 Runs
- 포지션 Runs
- 대체선수 Runs
- 타자 RAR/WAR
- 투수 RAR/WAR
- 팀 WAR 합

### 팀 소속 선수 순위

- 시즌별 선수 타격 기록·세이버·WAR
- 시즌별 투수 기록·FIP·ifFIP·gmLI·WAR
- 선수 행 더블클릭 시 pcode 기준 개인 페이지를 새 창으로 염

### 상대전적

상대 팀별로 다음 기록을 제공합니다.

```text
G / W / L / D / 승률
득점 / 실점 / 득실차
PA / AB / H / HR / BB / SO
AVG / OBP / SLG / OPS
IP / ER / ERA / FIP
```

상대 팀 행을 더블클릭하면 해당 상대 팀의 팀 페이지를 새 창으로 엽니다.

### 상황별

타격·투구 관점을 구분하여 다음 축을 제공합니다.

```text
주자 상황
아웃카운트
이닝 구간
점수차
득점권
클러치
홈/원정
선두타자
2아웃
```

현재 클러치 기준은 `7회 이후 AND 절대 점수차 3점 이내`입니다.

### 구종별 팀 타격

각 타석의 마지막 실제 투구 구종에 결과를 귀속합니다.

```text
구종 / PA / AB / H / 1B / 2B / 3B / HR
BB / HBP / SO / AVG / OBP / SLG / OPS / wOBA
```

### 구종별 팀 투구

```text
구종 / 투구 수 / 사용률 / 평균 구속 / 스트라이크%
Swing% / Whiff% / Contact% / CSW%
상대 PA / 상대 AB / 피안타 / 피홈런 / 피AVG / 피OPS
```

### 경기 로그와 계산 근거

- 날짜, 시즌, 상대, 홈/원정, 구장, 득점·실점, 결과, GameId
- 팀 지표·상대전적·상황별·구종별 계산 정책 설명

## 웹 포팅 준비

Application 프로젝트에 WinForms·SQLite 독립 계약을 추가했습니다.

```csharp
ITeamPageService
TeamPageData
TeamProfile
TeamBattingSeasonRow
TeamPitchingSeasonRow
TeamOpponentRecordRow
TeamSituationSplitRow
TeamPitchTypeBattingRow
TeamPitchTypePitchingRow
```

API 엔드포인트:

```http
GET /api/teams
GET /api/teams/{teamCode}
```

## SQLite 인덱스

```text
IX_Games_TeamSeasonDate
IX_PlateAppearances_BattingTeamGame
IX_PlateAppearances_FieldingTeamGame
```

기존 구종·투구 인덱스와 함께 팀 상대/상황/구종 집계에 사용됩니다. 기존 DB도 프로그램 초기화 시 `CREATE INDEX IF NOT EXISTS`로 자동 보강됩니다.

## 숫자 표시

- AVG/OBP/SLG/OPS/wOBA/피AVG/피OPS/승률: 소수 셋째 자리
- 사용률/스트라이크%/Swing%/Whiff%/Contact%/CSW%: 백분율 소수 첫째 자리
- ERA/FIP/ifFIP/dRPW/gmLI: 소수 둘째 자리
- WAR/RAR/구속/IP: 기존 야구 통계 형식 유지

## 정적 검증 결과

```text
C# 파일                              58개
프로젝트                              6개
객체 초기화 속성 대조               1,748건 PASS
enum 참조 대조                         385건 PASS
SQLite 응용 테이블                     21개
SQLite 인덱스                           44개
INSERT 구조 검사                        21개 PASS
전체 관계형 SQL 컴파일 검사             65개 PASS
팀 페이지 SQL 컴파일 검사                10개 PASS
팀 페이지 API/탭/인덱스 검사              PASS
7경기 샘플 kbo_r                         7/7
원본·정규화 경기 JSON 저장 열             0개
전체 V2 정적 검증                         PASS
ZIP 무결성                               PASS 예정
```

## 확인이 남은 부분

현재 작업 환경에는 .NET SDK, MSBuild, Windows Forms 런타임이 없어 실제 `dotnet build`와 Windows GUI 실행은 수행하지 못했습니다.

사용자 PC에서 다음 순서로 확인해야 합니다.

```text
NaverSabermetrics.V2.sln 열기
→ NuGet 패키지 복원
→ 솔루션 다시 빌드
→ NaverRelay.Gui 실행
→ DB 적재 또는 기존 V2 DB 연결
→ 팀 페이지 또는 Ctrl+T
```
