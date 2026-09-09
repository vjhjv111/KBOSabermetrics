# SQLite 관계형 통계 데이터웨어하우스 V2

## 목적

V2는 원본 JSON을 import 단계에서만 사용합니다.

```text
원본 JSON은 신규·변경 경기를 가져올 때 1회 사용
이후 모든 조회와 통계 계산은 SQLite 관계형 행만 사용
```

`Games.NormalizedJson` 같은 경기 전체 JSON 저장·재역직렬화 방식은 사용하지 않습니다.

## DB 파일

```text
%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_v2.db
```

다음 기존 DB와 완전히 분리됩니다.

```text
sabermetrics.db
sabermetrics_warehouse.db
```

## 가져오기 경계

GUI의 import 흐름:

```text
InputDocument.ReadJsonAsync
→ RelayParser.ParseJson
→ WarehouseProjectionBuilder.Build
→ DatabaseCacheService.SaveGameAndSourceAsync
→ 경기 객체 메모리 해제
```

경기 하나를 하나의 SQLite 트랜잭션으로 저장합니다. 같은 GameId가 있으면 외래키 `ON DELETE CASCADE`로 기존 하위 관계 행을 제거하고 새 결과로 교체합니다.

## 증분 판정

`ParsedSources`에는 원본 본문을 저장하지 않고 다음 메타데이터만 저장합니다.

```text
SourceKey
Fingerprint
GameId
SourceDisplay
ParserVersion
ParsedUtc
```

같은 지문과 같은 파서 버전이면 건너뜁니다. 파일 크기·수정 시각·ZIP 엔트리·파서 버전이 바뀌면 다시 가져옵니다.

## 스키마

### 시스템

- `Metadata`: 스키마 버전, DataVersion, 저장 방식
- `ParsedSources`: 증분 import 지문
- `ComputedCache`: 필터별 최종 DTO 캐시

### 경기·선수

- `Games`: 날짜, 팀, 점수, 구장, roundCode, 원본 요약 건수
- `GameSummaries`: 파서 검증 요약
- `Players`: pcode 기반 검색 인덱스
- `GamePlayers`: 경기별 팀·포지션·프로필 관찰값

### 정규화 사실

- `RelayGroups`
- `NormalizedEvents`
- `PlateAppearances`
- `Pitches`
- `RunnerEvents`
- `PlayerChanges`
- `AdministrativeEvents`
- `BattingGameLines`
- `PitchingGameLines`
- `Diagnostics`

### 빠른 통계 조회

- `BatterGameStats`: 선수·팀·경기 1행
- `PitcherGameStats`: 선수·팀·경기 1행
- `LeagueConstants`
- `ParkFactors`

경기 단위 집계는 시즌, 날짜 범위, 최근 N경기, 홈/원정, 상대팀, 구장 조회의 기본 단위입니다. 시즌 통계를 열 때 타석·투구 사실 테이블을 매번 전체 스캔하지 않습니다.

## JSON 저장 정책

DB 스키마에는 다음 열이 없습니다.

```text
NormalizedJson
RawJson
SourceJson
JsonBlob
```

`ComputedCache`에는 화면 DTO처럼 작은 계산 결과가 JSON으로 들어갈 수 있지만, 원본 경기 또는 정규화 경기 전체가 아닙니다.

## 조회 경로

### 통계 탭

```text
GameQuery
→ FilteredGames CTE
→ BatterGameStats / PitcherGameStats GROUP BY pcode, team
→ 통계 계산
→ 화면 DTO
```

동일한 `DataVersion + 필터` 결과는 `ComputedCache`에서 재사용합니다.

### 선수 페이지

```text
Players에서 pcode 검색
→ 해당 pcode의 경기 단위 통계 조회
→ 연도·팀별 집계
→ 필요한 원시 로그만 조회
```

### 상세 로그

타석·투구·주루·교체·관리·진단은 관계형 테이블에서 페이지 단위로 읽습니다.

## 정규시즌 기준

모든 정규시즌 전용 계산은 다음 조건만 사용합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode, ''))) = 'kbo_r'
```

카테고리명, `CompetitionType`, 파일명 추정만으로 정규시즌에 포함하지 않습니다.

## 리그 상수와 파크 팩터

새 경기 저장 또는 기존 경기 교체 시 `DataVersion`을 증가시키고 오래된 계산 캐시를 무효화합니다.

다음 조회에서 전체 `kbo_r` 관계 행을 기준으로 다시 계산해 저장합니다.

IFFB 포함 규칙:

```text
포수 파울플라이
1루수 파울플라이
2루수 뜬공
3루수 뜬공
유격수 뜬공
투수 뜬공
```

투수 IP/R/ER/HR/BB/HBP/SO는 경기별 최종 투수 라인을 사용합니다. 구원 레버리지 재료는 import 시 원본 승리확률/WPA 정보를 관계형 집계 필드로 보존합니다.

## SQLite 설정

초기화 시 주요 설정:

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=10000;
PRAGMA cache_size=-131072;
PRAGMA mmap_size=536870912;
```

새 DB에는 큰 페이지 크기를 적용한 뒤 스키마를 생성합니다.

## 최적화

`SQLite DB` 탭의 최적화 기능은 다음을 실행합니다.

```sql
ANALYZE;
PRAGMA optimize;
PRAGMA wal_checkpoint(TRUNCATE);
VACUUM;
```

`VACUUM`은 DB 크기만큼의 추가 임시 공간과 시간이 필요할 수 있습니다.

## 새 DB 재생성

1. 프로그램과 API를 종료합니다.
2. `reset-v2-db.bat`을 실행합니다.
3. 프로그램을 다시 실행합니다.
4. 원본 JSON/ZIP/폴더를 최초 한 번 적재합니다.

삭제 대상:

```text
sabermetrics_v2.db
sabermetrics_v2.db-wal
sabermetrics_v2.db-shm
```

## 웹 포팅

DB 접근은 `NaverRelay.Infrastructure.Sqlite`에 모여 있고 조회 계약은 `NaverRelay.Application`에 있습니다.

```text
WinForms GUI ─┐
              ├→ Application 계약 → SQLite Infrastructure
ASP.NET API ──┘
```

향후 PostgreSQL 구현은 동일 Application 계약을 구현하는 별도 Infrastructure 어댑터로 추가할 수 있습니다.
