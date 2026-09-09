# Naver Sabermetrics V2

네이버 KBO 경기 릴레이 JSON을 **신규·변경 경기에서 최초 한 번만** 파싱해 관계형 SQLite 데이터베이스에 적재하고, 그 이후의 프로그램 실행·탭 전환·선수 검색·기간별 통계·개인 페이지는 SQLite만 조회하는 .NET 8 솔루션입니다.

## 핵심 원칙

```text
원본 JSON / ZIP / 폴더
        ↓ 신규·변경 경기에서만 1회
NaverRelay.Parser
        ↓
관계형 SQLite 데이터웨어하우스
        ↓
Application 계약 + Statistics/Repository
        ├─ WinForms 데스크톱
        └─ ASP.NET Core Web API
```

다음 방식은 V2에서 사용하지 않습니다.

```text
DB에 경기 전체 JSON blob 저장
→ 탭을 열 때 JSON 재역직렬화
→ 모든 경기 객체를 메모리에 적재
```


## 투수 기록실

투수 기록실은 첨부 화면의 분류 순서를 따라 다음 탭을 제공합니다.

```text
기본 / 심화 / 가치 / 확장 / WP / 주자 / 선발 / 구원 / 타구 / 타구방향 / 투구 / 구종
```

선발·구원 역할별 WAR, 원본 WPA 기반 레버리지, 타구 유형·방향, 존 관리,
구종별 가치·구속·구사율·피AVG·피SLG를 관계형 SQLite에서 직접 조회합니다.
연봉·정밀 수비·현재 원본에서 안정적으로 확인할 수 없는 승패·세이브·홀드 기록은 임의 생성하지 않습니다.
자세한 계산 정의와 제한은 `docs/PITCHER_RECORD_ROOM.md`를 참고합니다.

## 데이터베이스 위치

기본 경로:

```text
%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_v2.db
```

일반적인 실제 경로:

```text
C:\Users\사용자명\AppData\Local\NaverSabermetrics\Data\sabermetrics_v2.db
```

V2는 기존 파일을 자동 변환하거나 읽지 않습니다.

```text
sabermetrics.db
sabermetrics_warehouse.db
```

새로운 `sabermetrics_v2.db`를 원본 데이터에서 다시 생성하도록 분리했습니다.

## 시작 방법

1. 압축을 새 폴더에 풉니다.
2. Visual Studio 2022에서 `NaverSabermetrics.V2.sln` 또는 `NaverSabermetrics.sln`을 엽니다.
3. `NaverRelay.Gui`를 시작 프로젝트로 지정합니다.
4. NuGet 패키지를 복원합니다.
5. 솔루션을 다시 빌드합니다.
6. `F5`로 실행합니다.
7. 5년치 JSON·ZIP·폴더를 최초 한 번 선택해 가져옵니다.

요구 환경:

- Windows 10/11
- Visual Studio 2022
- `.NET 데스크톱 개발` 워크로드
- .NET 8 SDK
- NuGet 패키지 `Microsoft.Data.Sqlite 8.0.29`

프로젝트 루트의 다음 배치 파일도 사용할 수 있습니다.

```text
build.bat                  NuGet 복원 + Release 빌드
clean-build.bat            bin/obj 삭제 후 전체 빌드
verify-v2.bat              빌드 + 7경기 파서 기준 검증
run-gui.bat                WinForms 실행
run-api.bat                ASP.NET Core API 실행
publish-win-x64.bat        Windows x64 배포 폴더 생성
reset-v2-db.bat            V2 DB 초기화
```

## 최초 적재와 증분 업데이트

각 원본 문서는 다음 순서로 처리됩니다.

```text
원본 1경기 읽기
→ 정규화 파싱
→ 관계형 행 생성
→ 한 SQLite 트랜잭션으로 저장
→ 경기 객체 메모리 해제
→ 다음 경기
```

`ParsedSources`에는 원본 본문이 아니라 지문만 저장합니다.

```text
SourceKey
Fingerprint
GameId
SourceDisplay
ParserVersion
ParsedUtc
```

처리 기준:

```text
같은 원본·같은 파서 버전  → 건너뜀
새 경기                    → 삽입
내용이 바뀐 기존 GameId    → 기존 관계 행 삭제 후 재삽입
파서 버전 변경             → 다시 적재
```

따라서 5년치를 한 번 적재한 뒤에는 다음 날 새 경기 파일만 추가하면 됩니다.

## 관계형 SQLite 스키마

### 경기·선수

```text
Metadata
ParsedSources
Games
GameSummaries
Players
GamePlayers
```

### 원시 사실 테이블

```text
RelayGroups
NormalizedEvents
PlateAppearances
Pitches
RunnerEvents
PlayerChanges
AdministrativeEvents
BattingGameLines
PitchingGameLines
Diagnostics
```

### 빠른 통계 조회용

```text
BatterGameStats
PitcherGameStats
LeagueConstants
ParkFactors
ComputedCache
```

`BatterGameStats`와 `PitcherGameStats`는 선수·팀·경기별 한 행입니다. 기간별 wRC+, 최근 N경기, 연도별 기록, 홈/원정, 상대팀, 구장 등의 통계는 이 행을 SQL로 합산합니다.

원본 또는 정규화 경기 JSON을 저장하는 열은 없습니다.

## 프로그램 재실행과 탭 전환

프로그램 시작 시에는 다음처럼 작은 메타데이터만 읽습니다.

```text
경기 수
사용 가능한 연도
최소·최대 날짜
팀 목록
구장 목록
선수 검색 인덱스
```

탭별 동작:

```text
경기 목록                → Games 조회
클래식·세이버·WAR       → 경기 단위 집계 테이블 SQL 합산
선수 검색                → Players 조회
선수 개인 페이지         → 해당 pcode의 행만 조회
타석·투구·주루 로그      → 관계형 원시 테이블 페이지 조회
동일 필터 재조회          → ComputedCache 사용
리그 상수·파크 팩터      → kbo_r 데이터 최초 계산 후 DB 저장
```

조회 과정에서 원본 JSON을 다시 열거나 `NormalizedGame` 전체를 재생성하지 않습니다.

## 정규시즌 기준

정규시즌은 정확히 다음 값만 인정합니다.

```text
roundCode == "kbo_r"
```

SQL에서는 다음 조건을 사용합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode, ''))) = 'kbo_r'
```

리그 상수와 파크 팩터도 전체 적재 데이터 중 `kbo_r` 경기만 사용합니다. 화면의 연도·팀·기간·규정 필터는 표시할 선수 기록 범위를 바꾸지만, 리그 기준 환경을 다른 경기 종류와 섞지 않습니다.

## 제공 기능

### 데이터 관리

- 단일 JSON, JSON 폴더, ZIP 입력
- 신규·변경 경기 증분 적재
- 진행률, 취소, 오류 로그
- 관계형 SQLite DB 상태 및 경로 표시
- DB 최적화와 재생성 도구

### 통계 화면

- 타자·투수 클래식 기록
- wOBA, wRAA, wRC, wRC+, OPS+
- FIP, xFIP, ifFIP, FIPR9, pFIPR9, 동적 dRPW
- FanGraphs형 포지션 보정
- KBO 보정형 투수 fWAR v3 / RA9-WAR / Blend WAR
- Swing%, Contact%, Whiff%, CSW%, 존 지표
- 리그 상수와 다년 파크 팩터
- 공식·계산기 탭

### 필터

- 연도와 경기 구분
- 팀과 포지션
- 규정타석·규정이닝 충족 비율
- 직접 날짜 범위
- 최근 7/14/30/60/90일
- 전반기·후반기
- 최근 5/10/20/30경기
- 상대팀, 홈/원정, 요일, 구장

### 선수 페이지

- 이름 또는 pcode 검색
- 동명이인: pcode·생년월일·소속팀·포지션으로 선택
- 연도·팀별 타격·투구 기록
- Value·WAR
- 경기·타석·투구 로그
- Rolling 7/15/30경기 wRC+
- 계산 근거 표시

### 팀 페이지

- 메인 도구모음의 `팀 페이지` 버튼 또는 `Ctrl+T`로 별도 창 열기
- 정규시즌 팀 목록과 팀명 이력 조회
- 연도별 팀 타격·투구·Value·WAR
- 시즌별 선수 타격·투구 순위, 선수 행 더블클릭으로 개인 페이지 이동
- 상대 팀별 승패·득실차·팀 타격·팀 투구 성적
- 주자·아웃·이닝·점수차·득점권·클러치·홈/원정 상황별 기록
- 마지막 투구 구종 기준 팀 타격 AVG/OBP/SLG/OPS/wOBA
- 팀 투수 구종별 사용률·평균 구속·Swing%·Whiff%·Contact%·CSW%·피AVG·피OPS
- 경기 로그와 계산 근거 표시

팀 페이지와 소속 이력은 정확히 `roundCode == kbo_r`인 경기만 사용합니다. 올스타전의 임시 팀 코드는 정규 구단 이력과 팀 페이지 목록에 섞이지 않습니다.

## 세이버메트릭스 정책

### IFFB

다음 결과를 모두 내야 뜬공으로 집계합니다.

```text
포수 파울플라이
1루수 파울플라이
2루수 뜬공
3루수 뜬공
유격수 뜬공
투수 뜬공
```

### 투수 최종 기록

IP, R, ER, HR, BB, HBP, SO는 원본 경기별 최종 투수 라인을 사용합니다. 선수 식별은 이름이 아니라 pcode를 사용합니다.

### gmLI

구원투수 교체 상황과 원본 `metricOption.wpaByPlate`를 이용해 경기 단위 레버리지 재료를 저장하고, 투수 WAR 계산에 LI 배수를 적용합니다.

### 파크 팩터

여러 연도의 동일 구조 JSON을 적재하면 구장별 FIP 구성요소 기반 팩터를 계산해 `ParkFactors`에 저장합니다.

## 솔루션 구성

```text
NaverSabermetrics.V2.sln
├─ NaverRelay.Parser
│  └─ 네이버 원본 DTO, 정규화 파서, 상태 머신, 검증
├─ NaverRelay.Application
│  └─ UI/DB 독립 조회 계약, 선수 페이지 DTO, 통계 표시 DTO
├─ NaverRelay.Infrastructure.Sqlite
│  └─ 관계형 스키마, import, repository, 통계·선수 페이지 조회
├─ NaverRelay.Gui
│  └─ WinForms 데스크톱 UI
├─ NaverRelay.Api
│  └─ ASP.NET Core Web API
└─ NaverRelay.Cli
   └─ 독립 파서 검증·정규화 도구
```

의존 방향:

```text
Parser
  ↑
Application
  ↑
Infrastructure.Sqlite
  ↑              ↑
WinForms GUI     ASP.NET Core API
```

WinForms와 API는 동일한 `sabermetrics_v2.db`와 Application 계약을 사용할 수 있습니다. 추후 PostgreSQL 구현을 추가할 때는 Application 계약을 유지하고 Infrastructure 구현을 교체하는 방식으로 확장할 수 있습니다.

## Web API

`run-api.bat`을 실행하면 기본적으로 다음 주소를 사용합니다.

```text
http://localhost:5080
```

현재 엔드포인트:

```http
GET /api/health
GET /api/catalog
GET /api/games
GET /api/stats/batters
GET /api/stats/pitchers
GET /api/players/search?q=이승현
GET /api/players/51454
GET /api/teams
GET /api/teams/SS
```

사용 DB를 직접 지정하려면 환경 변수 또는 `appsettings.json`을 사용할 수 있습니다.

```text
NAVER_SABERMETRICS_DB=C:\data\sabermetrics_v2.db
```

## 7경기 검증 기준

`SampleData/2026.zip`의 기대값:

| 항목 | 값 |
|---|---:|
| 경기 | 7 |
| 원본 릴레이 그룹 | 666 |
| 원본 이벤트 | 3,646 |
| 완료 타석 | 522 |
| 중단 타석 | 1 |
| 실제 투구 | 1,997 |
| PTS 연결 | 1,996 |
| PTS 누락 | 1 |
| 주루 이벤트 | 193 |
| 선수 교체 | 120 |
| 관리 이벤트 | 141 |
| 파서 경고 | 1 |
| 파서 오류 | 0 |

CLI 검증:

```powershell
dotnet run --project src/NaverRelay.Cli -- SampleData/2026.zip validation\smoke-output --validate-known-sample
```

정상 결과:

```text
Known 7-game sample validation: PASS
```

## 문서

- `docs/V2_ARCHITECTURE.md`: V2 계층과 의존 방향
- `docs/V2_BUILD_AND_SMOKE_TEST.md`: Visual Studio 빌드·초기 적재·검증 절차
- `docs/V2_IMPLEMENTATION_REPORT.md`: 구현·정적 검증 요약
- `docs/SQLITE_RELATIONAL_WAREHOUSE.md`: DB 스키마와 조회 방식
- `docs/PLAYER_PAGES_AND_WEB_PORTING.md`: 선수 페이지와 웹 포팅
- `docs/TEAM_PAGES.md`: 팀 페이지, 상대전적, 상황별·구종별 집계
- `docs/TEAM_PAGES_IMPLEMENTATION_REPORT.md`: 팀 페이지 구현·검증 보고서
- `docs/POSTGRESQL_PORTING_PLAN.md`: 서버 DB 이전 계획
- `docs/FANGRAPHS_PITCHER_WAR_V2.md`: 기존 투수 WAR 계산
- `docs/KBO_PITCHER_WAR_V3.md`: KBO 역할별 대체수준, WARIP, RA9-WAR, Blend WAR
- `docs/FANGRAPHS_POSITION_ADJUSTMENT.md`: 야수 포지션 보정
- `docs/FILTERS_AND_QUALIFICATION.md`: 필터와 규정 기준
- `docs/LIMITATIONS.md`: 현재 데이터·공식의 한계
- `validation/v2-architecture-validation.json`: V2 아키텍처 검사 결과
- `validation/static-source-validation-v2.json`: 정적 소스 검사 결과
- `validation/relational-warehouse-validation.json`: SQLite 스키마·SQL 검사 결과

## 확인이 필요한 마지막 단계

이 패키지는 구조·스키마·SQL·참조 관계를 정적으로 검사했습니다. 실제 Windows 컴파일러 검증은 사용자 PC에서 다음 순서로 실행해야 합니다.

```text
verify-v2.bat
```

또는 Visual Studio에서:

```text
NuGet 패키지 복원
→ 솔루션 다시 빌드
→ NaverRelay.Gui 실행
→ 7경기 샘플 적재
→ 선수 검색 및 탭 전환 확인
```
