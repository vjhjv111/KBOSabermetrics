# SQLite 관계형 데이터웨어하우스 전환 보고서

작성일: 2026-08-06

## 1. 구현 목적

이 버전은 네이버 경기 JSON을 조회용 캐시로 반복 역직렬화하던 중간 구조를 제거하고, 다음 흐름으로 변경했습니다.

```text
원본 JSON/ZIP
    ↓ 신규·변경 경기 import 시에만 1회 읽기
RelayParser 정규화
    ↓
관계형 SQLite 행으로 저장
    ↓
이후 프로그램 실행·탭 전환·선수 페이지·기간 조회는 SQLite SQL만 사용
```

원본 경기 JSON 또는 `NormalizedGame` 전체를 DB 열에 저장하지 않습니다.

## 2. 새 DB 파일

```text
%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_warehouse.db
```

기존 중간 버전의 `sabermetrics.db`는 자동 변환하거나 읽지 않습니다. 사용자가 새 DB를 만들어도 된다고 했기 때문에 별도 파일명으로 완전히 분리했습니다.

프로젝트 루트의 `reset-warehouse-db.bat`을 실행하면 확인 후 다음 세 파일을 지울 수 있습니다.

```text
sabermetrics_warehouse.db
sabermetrics_warehouse.db-wal
sabermetrics_warehouse.db-shm
```

## 3. 최초 import 및 증분 갱신

한 경기 import 과정:

```text
InputDocument.ReadJsonAsync
→ RelayParser.ParseJson
→ WarehouseProjectionBuilder.Build
→ 한 경기 SQLite 트랜잭션 저장
→ NormalizedGame 객체 해제
```

`ParsedSources`에는 JSON 본문이 아니라 원본 식별용 메타데이터만 저장합니다.

```text
SourceKey
Fingerprint
GameId
SourceDisplay
ParserVersion
ParsedUtc
```

동작:

```text
같은 원본·같은 파서 버전  → 파싱 생략
새 경기                    → 관계형 행 삽입
수정된 기존 경기           → GameId의 기존 하위 행 삭제 후 새 결과 삽입
```

경기 삭제는 외래키 `ON DELETE CASCADE`로 처리하며, 교체 작업 전체를 한 트랜잭션으로 실행합니다.

## 4. 관계형 스키마

### 메타데이터·경기·선수

- `Metadata`
- `ParsedSources`
- `Games`
- `GameSummaries`
- `Players`
- `GamePlayers`

### 원시 사실 테이블

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

### 빠른 통계 집계 테이블

- `BatterGameStats`: 선수·팀·경기당 1행
- `PitcherGameStats`: 선수·팀·경기당 1행

### 계산 결과 캐시

- `LeagueConstants`
- `ParkFactors`
- `ComputedCache`

총 21개 응용 테이블과 36개 보조 인덱스를 생성합니다.

## 5. 조회 방식

### 프로그램 시작

DB에서 다음 작은 메타데이터만 읽습니다.

```text
경기 수
연도 목록
팀 목록
구장 목록
선수 검색 인덱스
```

경기 JSON 또는 `NormalizedGame` 전체를 만들지 않습니다.

### 클래식·세이버·WAR 탭

```text
GameQuery
→ Games 필터 CTE
→ BatterGameStats/PitcherGameStats SQL SUM·GROUP BY
→ 화면 DTO 계산
→ ComputedCache 저장
```

같은 데이터 버전과 같은 필터를 다시 조회하면 최종 화면 DTO 캐시를 사용합니다.

### 기간·최근 N경기

경기 단위 집계 행만 날짜 또는 최근 경기 조건으로 합산합니다. 타석·투구 원시 행 전체를 다시 순회하지 않습니다.

### 선수 검색·개인 페이지

- `Players`에서 pcode·이름·팀 검색
- 동명이인은 pcode, 생년월일, 최근 팀, 포지션으로 구분
- 해당 pcode의 경기 단위 행과 로그 행만 조회
- 연도별 기록과 Rolling 7/15/30경기 wRC+ 구성

### 상세 로그

다음 원시 테이블을 직접 페이지 조회합니다.

```text
PlateAppearances
Pitches
RunnerEvents
PlayerChanges
AdministrativeEvents
Diagnostics
```

GUI 한 페이지는 최대 5,000행입니다.

## 6. 정규시즌·리그 환경 기준

정규시즌은 추정 카테고리명이 아니라 정확히 아래 조건만 사용합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode, ''))) = 'kbo_r'
```

리그 상수와 파크 팩터도 전체 적재 데이터 중 `kbo_r` 경기만 사용합니다.

IFFB 규칙:

```text
포수 파울플라이
1루수 파울플라이
2루수 뜬공
3루수 뜬공
유격수 뜬공
투수 뜬공
```

투수의 IP/R/ER/HR/BB/HBP/SO는 경기별 최종 투수 기록을 사용합니다. 구원 gmLI 계산 재료는 import 시 원본 `metricOption.wpaByPlate`에서 작은 집계값으로 저장합니다.

## 7. DB에 저장하지 않는 것

다음 열 또는 저장 경로는 없습니다.

```text
NormalizedJson
RawJson
SourceJson
JsonBlob
```

`ComputedCache.JsonValue`에는 필터별 최종 표 DTO처럼 작은 계산 결과만 들어갑니다. 경기 원본이나 정규화 경기 전체가 아닙니다.

`파싱과 동시에 정규화 JSON 저장` 옵션은 별도 파일 출력 기능일 뿐이며 기본값은 꺼져 있고 GUI 조회에 사용되지 않습니다.

## 8. SQLite 설정

새 DB에 다음 설정을 적용합니다.

```text
page_size = 32768
journal_mode = WAL
synchronous = NORMAL
temp_store = MEMORY
foreign_keys = ON
busy_timeout = 10000
cache_size ≈ 128MB
mmap_size = 512MB
```

`SQLite DB` 탭의 최적화 버튼은 다음을 실행합니다.

```sql
ANALYZE;
PRAGMA optimize;
PRAGMA wal_checkpoint(TRUNCATE);
VACUUM;
```

## 9. 웹 포팅 고려

솔루션은 다음 프로젝트로 나뉩니다.

```text
NaverRelay.Parser
NaverRelay.Application
NaverRelay.Gui
NaverRelay.Cli
```

현재 WinForms 조회 서비스는 관계형 SQLite 행만 사용합니다. 다음 단계에서 Repository 인터페이스를 Application 계층으로 올리고 ASP.NET Core API 또는 PostgreSQL 구현을 추가할 수 있습니다.

## 10. 검증 결과

### C# 정적 구조 검사

```text
C# 파일                         50개
프로젝트                         4개
객체 초기화 속성 대조         1,399건
enum 멤버 참조 대조             382건
실패                               0건
결과                              PASS
```

### SQLite·SQL·아키텍처 검사

```text
응용 테이블                     21개
보조 인덱스                     36개
INSERT 형태 검사                21개
SQLite SQL 컴파일 검사          47개
원본/정규화 JSON 열              0개
결과                              PASS
```

### 포함된 7경기 원본 확인

```text
경기                              7
roundCode=kbo_r                    7
투수 최종 IP·ER 기록 포함 경기    7
metricOption 포함 경기             7
문제                               0
```

검증 파일:

```text
validation/static-source-validation-relational-warehouse.json
validation/relational-warehouse-validation.json
```

## 11. 컴파일 확인 상태

현재 작업 컨테이너에는 .NET SDK/MSBuild가 설치되어 있지 않고 외부 SDK 바이너리 다운로드도 허용되지 않아 실제 `dotnet build`는 실행하지 못했습니다.

대신 프로젝트 XML, 솔루션 경로, C# 구문 구분자, 객체 초기화 속성, enum 참조, SQLite DDL, INSERT 열 개수, 런타임 SELECT SQL 및 DB 전용 조회 경계를 정적으로 점검했습니다.

최종 확인 절차:

```text
Visual Studio 2022에서 NaverSabermetrics.sln 열기
→ NuGet 패키지 복원
→ NaverRelay.Gui 시작 프로젝트 설정
→ 솔루션 다시 빌드
→ F5 실행
→ 7경기 샘플로 먼저 확인
→ 새 관계형 DB를 초기화하고 5년치 원본 1회 import
```

## 12. 기대 동작

최초 5년치 import 후에는:

```text
프로그램 재실행          → SQLite 메타데이터만 읽기
탭 전환                  → 관계형 집계 SQL 또는 ComputedCache
선수 페이지              → 해당 pcode 행만 조회
기간별 wRC+/WAR          → 선수·경기 집계 행 합산
경기·타석·투구 로그      → 관계 테이블 페이지 조회
원본 JSON 재읽기          → 없음
NormalizedGame 역직렬화   → 없음
```

다만 실제 응답 시간과 최종 DB 크기는 전체 경기 수, 원본 세부 이벤트 수, 저장장치 속도 및 PC 사양에 따라 달라집니다.
