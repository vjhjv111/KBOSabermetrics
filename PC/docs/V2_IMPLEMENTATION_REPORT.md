# Naver Sabermetrics V2 구현·검증 보고서

작성일: 2026-08-06

## 1. 결과 요약

기존 기능 누적형 프로젝트를 다음 6개 프로젝트로 재구성했습니다.

```text
NaverRelay.Parser
NaverRelay.Application
NaverRelay.Infrastructure.Sqlite
NaverRelay.Gui
NaverRelay.Api
NaverRelay.Cli
```

핵심 저장 정책은 다음과 같습니다.

```text
원본 JSON은 신규·변경 경기 import 때만 사용
→ 관계형 SQLite 행 저장
→ 이후 모든 조회는 DB만 사용
```

V2 DB:

```text
%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_v2.db
```

기존 `sabermetrics.db`, `sabermetrics_warehouse.db`는 읽지 않습니다.

## 2. JSON blob 제거

관계형 스키마에 다음 열이 없음을 검사했습니다.

```text
NormalizedJson
RawJson
SourceJson
JsonBlob
```

정상 조회 경로에도 `RelayParser.ParseJson()`이나 `JsonSerializer.Deserialize<NormalizedGame>()`가 없습니다. 파서는 GUI import와 CLI 파서 검증 경로에만 존재합니다.

## 3. 관계형 테이블

응용 테이블 21개를 생성합니다.

```text
Metadata
ParsedSources
Games
GameSummaries
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
GamePlayers
Players
BatterGameStats
PitcherGameStats
ComputedCache
LeagueConstants
ParkFactors
```

보조 인덱스는 검사 스크립트 기준 36개이며, 전체 스키마 실행 검사 기준 SQLite 내부/추가 인덱스를 포함해 56개가 생성됩니다.

## 4. 조회 방식

### 프로그램 시작

작은 카탈로그만 조회합니다.

```text
경기 수
연도
날짜 범위
팀
구장
```

### 통계 탭

`BatterGameStats`, `PitcherGameStats`를 필터 조건에 맞춰 SQL 합산합니다.

### 선수 페이지

`Players`에서 pcode를 검색하고 해당 선수의 경기 단위 행과 로그만 조회합니다.

### 상세 로그

타석·투구·주루·교체·진단 관계 테이블을 페이지 단위로 조회합니다.

### 캐시

동일한 `DataVersion + 필터` 결과는 `ComputedCache`에 저장됩니다. 신규·변경 경기 import 시 DataVersion이 증가해 오래된 결과를 무효화합니다.

## 5. 정규시즌 기준

다음 값만 정규시즌으로 처리합니다.

```text
roundCode == "kbo_r"
```

SQL:

```sql
LOWER(TRIM(COALESCE(RoundCode, ''))) = 'kbo_r'
```

리그 상수와 파크 팩터도 같은 기준을 사용합니다.

## 6. 세이버메트릭스 기능

- wOBA, wRAA, wRC, wRC+, OPS+
- FIP, xFIP, ifFIP, FIPR9, pFIPR9
- 동적 dRPW
- FanGraphs형 포지션 보정
- 선발·구원 대체수준
- 원본 WPA 기반 gmLI와 LI 배수
- FanGraphs형 투수 WAR v2
- 다년 파크 팩터
- 기간·최근 N경기·팀·포지션·규정 필터
- 선수 연도별 기록과 Rolling wRC+

IFFB 포함 문장:

```text
포수 파울플라이
1루수 파울플라이
2루수 뜬공
3루수 뜬공
유격수 뜬공
투수 뜬공
```

## 7. 웹 포팅 준비

`NaverRelay.Api` ASP.NET Core 프로젝트를 포함했습니다.

현재 경로:

```text
/api/health
/api/catalog
/api/games
/api/stats/batters
/api/stats/pitchers
/api/players/search
/api/players/{pcode}
```

WinForms와 API는 같은 Application 계약과 SQLite Infrastructure를 사용합니다. PostgreSQL 포팅은 별도 Infrastructure 구현을 추가하는 방향으로 문서화했습니다.

## 8. 기존 오류 회귀 방지

다음 오류를 방지하는 검사를 포함했습니다.

- `Application.Run` 네임스페이스 충돌
- `ShortcutKeys = Keys.Escape` 예외
- Designer 초기 `SplitterDistance` 예외
- `double`/`null` 조건식 CS0173
- 로컬 함수의 `out` 매개변수 캡처 CS1628
- GUI의 직접 SQLite 패키지 의존
- API의 WinForms 참조

## 9. 정적 검증 결과

| 검사 | 결과 |
|---|---|
| V2 프로젝트 구성 6개 | PASS |
| C# 파일 | 52개 |
| C# 소스 줄 수 | 약 15,620줄 |
| 객체 초기화 속성 대조 | 1,399건 PASS |
| enum 참조 대조 | 382건 PASS |
| C# 구분자 균형 | PASS |
| 프로젝트 XML | PASS |
| 솔루션 프로젝트 경로 | PASS |
| 프로젝트 의존 방향 | PASS |
| JSON blob 스키마 열 | 0개 |
| SQLite 응용 테이블 | 21개 |
| INSERT 형태 검사 | 21개 PASS |
| SQL 컴파일 검사 | 47개 PASS |
| 7경기 원본 구조 검사 | PASS |
| 동명이인 pcode 분리 | PASS |
| 투수 최종 라인 필드 | 72/72 PASS |

현재 검증 파일:

```text
validation/v2-architecture-validation.json
validation/static-source-validation-v2.json
validation/relational-warehouse-validation.json
validation/player-profile-data-validation.json
```

## 10. 7경기 파서 기준

| 항목 | 예상값 |
|---|---:|
| 경기 | 7 |
| 릴레이 그룹 | 666 |
| 원본 이벤트 | 3,646 |
| 완료 타석 | 522 |
| 중단 타석 | 1 |
| 투구 | 1,997 |
| PTS 연결 | 1,996 |
| PTS 누락 | 1 |
| 주루 이벤트 | 193 |
| 선수 교체 | 120 |
| 관리 이벤트 | 141 |
| 경고 | 1 |
| 오류 | 0 |

## 11. 사용자 환경에서 실행할 검증

```text
verify-v2.bat
```

수행 항목:

```text
NuGet 복원
Release 전체 빌드
7경기 파서 실행
알려진 기준값 검사
```

## 12. 확인하지 못한 항목

제작 컨테이너에는 .NET SDK, MSBuild, C# 컴파일러가 없고 외부 SDK 다운로드도 불가능해 실제 `dotnet build`를 실행하지 못했습니다.

따라서 정적 검사와 SQLite SQL 컴파일 검사는 통과했지만, 사용자 Windows 환경의 Visual Studio에서 `verify-v2.bat` 또는 솔루션 다시 빌드로 최종 컴파일을 확인해야 합니다.
