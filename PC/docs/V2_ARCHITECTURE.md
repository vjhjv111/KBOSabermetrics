# Naver Sabermetrics V2 아키텍처

## 목표

V2의 가장 중요한 목표는 원본 JSON의 역할을 import 입력으로 제한하는 것입니다.

```text
JSON 1회 파싱
→ 관계형 사실 데이터 저장
→ 경기 단위 집계 저장
→ 모든 화면은 SQL 조회
```

DB에 경기 전체 JSON을 저장하거나, 화면을 열 때 다시 역직렬화하지 않습니다.

## 프로젝트 경계

### NaverRelay.Parser

책임:

- 네이버 응답 DTO
- JSON 역직렬화
- 릴레이 그룹 분류
- 타석·투구·주루·교체 정규화
- PTS 계산
- 경기 최종 라인 검증

의존성:

- 다른 솔루션 프로젝트에 의존하지 않음

### NaverRelay.Application

책임:

- `GameQuery`
- DB 조회 계약
- 통계 조회 계약
- 선수 검색·개인 페이지 계약
- UI 중립 DTO

금지:

- WinForms 참조
- SQLite 참조
- ASP.NET Core 참조

### NaverRelay.Infrastructure.Sqlite

책임:

- SQLite 스키마 생성
- 신규·변경 원본 판정
- 정규화 경기의 관계형 저장
- 통계용 SQL 집계
- 선수 페이지 조회
- 리그 상수·파크 팩터·계산 결과 캐시

이 프로젝트만 `Microsoft.Data.Sqlite` 패키지를 참조합니다.

### NaverRelay.Gui

책임:

- 파일·폴더·ZIP 선택
- import 진행률과 취소
- 통계 탭과 표
- 선수 검색·개인 페이지
- DB 관리 화면

GUI는 SQL 문이나 SQLite 패키지에 직접 의존하지 않고 Infrastructure 서비스와 Application DTO를 사용합니다.

### NaverRelay.Api

책임:

- Application/Infrastructure를 HTTP로 노출
- 향후 React/Next.js 프런트엔드의 백엔드

WinForms를 참조하지 않습니다.

### NaverRelay.Cli

책임:

- 파서 독립 검증
- 정규화 JSON 출력
- 알려진 7경기 기준 검사

## 저장 계층

### 사실 데이터

```text
Games
PlateAppearances
Pitches
RunnerEvents
PlayerChanges
AdministrativeEvents
BattingGameLines
PitchingGameLines
```

통계 공식이 바뀌어도 원본 JSON을 다시 읽지 않고 관계형 사실 행에서 재계산할 수 있도록 보존합니다.

### 경기 단위 집계

```text
BatterGameStats
PitcherGameStats
```

날짜 범위 또는 최근 N경기 통계의 기본 단위입니다.

### 계산 결과

```text
LeagueConstants
ParkFactors
ComputedCache
```

새 경기 또는 수정 경기 적재 시 `DataVersion`을 올려 오래된 계산 결과를 무효화합니다.

## Import 흐름

```text
InputDocument
→ ReadJsonAsync
→ RelayParser.ParseJson
→ WarehouseProjectionBuilder
→ DatabaseCacheService.SaveGameAndSourceAsync
```

경기 하나를 하나의 트랜잭션으로 저장합니다. 같은 GameId를 교체할 때는 외래키 cascade를 이용해 기존 하위 행을 정리한 뒤 새 결과를 적재합니다.

## 조회 흐름

### 리그/선수 표

```text
GameQuery
→ SQL 필터
→ 경기 단위 집계 GROUP BY
→ Statistics Engine
→ 화면 DTO
```

### 선수 페이지

```text
pcode
→ Players
→ 해당 선수의 BatterGameStats/PitcherGameStats
→ 해당 선수의 원시 로그
→ 연도별·Rolling 결과
```

### 상세 로그

```text
LIMIT/OFFSET 또는 페이지 키
→ 현재 페이지 행만 반환
```

## 웹 포팅

현재 API는 데스크톱과 동일한 Application 계약과 SQLite 구현을 사용합니다.

```text
React/Next.js
     ↓ HTTP
ASP.NET Core API
     ↓
Application 계약
     ↓
SQLite Infrastructure
```

향후 서버용 PostgreSQL 구현을 추가할 때:

```text
IWarehouseReadService
IAnalyticsQueryService
IPlayerPageService
```

계약은 유지하고 Infrastructure 구현을 교체합니다.

## 금지할 회귀

- Games에 `NormalizedJson`, `RawJson`, `JsonBlob` 열 추가
- 시작 시 모든 경기 객체 로드
- 탭 변경 시 원본 JSON 읽기
- 이름을 선수 기본키로 사용
- `CompetitionType` 추정값만으로 정규시즌 판정
- WinForms에서 SQL 직접 실행
