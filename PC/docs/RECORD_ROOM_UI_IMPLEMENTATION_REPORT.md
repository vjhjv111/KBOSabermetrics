# Naver Sabermetrics V2 기록실 UI 개편 보고서

## 기준 자료

사용자가 제공한 기록실 화면 묶음의 상단 기록실 구조와 타자 세부 탭을 기준으로 WinForms 메인 화면을 재구성했습니다. 연봉 및 수비처럼 현재 관계형 DB가 제공하지 않는 데이터는 표시 대상에서 제외했습니다.

## 시작 화면 구조

프로그램 시작 화면을 기존의 많은 기능 탭 대신 기록실 중심 화면으로 변경했습니다.

```text
시즌기록실
통산기록실
팀기록실
연도별 상수
```

시즌·통산·팀 기록실은 다시 다음 역할로 구분됩니다.

```text
타자
투수
```

현재 타자 화면은 제공된 스크린샷 구조에 맞춰 우선 개편했고, 투수 화면은 추후 제공될 투수 스크린샷을 반영할 수 있도록 별도의 역할·하위 탭 구조만 유지했습니다.

## 타자 하위 탭

다음 순서로 구성했습니다.

```text
기본
심화
가치
확장
클러치
파워
팀배팅
도루
주루
타구
타구방향
투구
구종
```

### 기본

G, WAR, PA, ePA, AB, R, H, 2B, 3B, HR, TB, RBI, SB, CS, BB, HBP, IBB, SO, GDP, SH, SF, AVG, OBP, SLG, OPS, R/ePA, wRC+를 표시합니다.

### 심화

K%, BB%, BB/K, BABIP, IsoP, IsoD, R/ePA, wOBA, wRC, RC27, wRC+, OPS+를 표시합니다.

### 가치

타격 RAA, 주루 RAA, FanGraphs형 포지션 보정, 공격 Runs, 대체선수 Runs, RAR, RPW, WAR를 표시합니다. 현재 확보되지 않는 정밀 수비 가치는 제외했습니다.

### 확장

PSN, TotA, SecA, RC, RC27, BB/K, ISO를 표시합니다.

### 클러치

7회 이후이면서 절대 점수차 3점 이내인 타석을 클러치 상황으로 정의하고, PA·타율·출루율·장타율·OPS·WPA 계열 결과를 DB에서 직접 조회합니다.

### 파워·팀배팅·도루·주루

현재 원시 관계형 데이터로 계산 가능한 범위만 표시하며 연봉 및 수비 관련 열은 만들지 않았습니다.

### 타구·타구방향

땅볼, 뜬공, 직선타, 내야 뜬공, HR/FB, 번트 및 좌·좌중·중·우중·우 방향 기록을 PlateAppearances에서 직접 집계합니다.

### 투구·구종

타자의 스윙·컨택·헛스윙·CSW·존 안팎 기록과, 타석의 마지막 실제 투구 구종 기준 타격 결과를 표시합니다.

## 선수명 오른쪽 적용 조건 열

시즌기록실과 통산기록실의 선수 그리드에는 `Name` 바로 오른쪽에 `적용 조건` 열을 동적으로 추가합니다.

표시 예:

```text
2026 · 정규시즌 · 팀 SS · Pos 3B · 규정 50% · 최근 30일 · vs LG · 홈 · 구장 대구
```

이 열은 DTO나 DB 스키마에 저장하지 않는 화면 전용 열이며, 정렬 후에도 셀 포맷팅 단계에서 유지됩니다. 팀 단위 그리드에는 표시하지 않습니다.

## 기록실별 집계 단위

### 시즌기록실

선수와 팀 조합 단위로 선택한 한 시즌을 조회합니다.

### 통산기록실

선수 코드를 기준으로 모든 선택 기간을 합산합니다. 소속팀 표시는 정규시즌 이력상 최근 팀을 사용합니다.

### 팀기록실

선수 대신 팀 단위로 집계합니다. 팀 단위에서 의미가 없는 주 포지션 열은 숨깁니다.

### 연도별 상수

읽어온 전체 데이터 중 다음 조건에 해당하는 경기만 사용합니다.

```sql
LOWER(TRIM(COALESCE(RoundCode, ''))) = 'kbo_r'
```

리그 상수와 파크 팩터를 별도 하위 탭에서 표시합니다.

## 필터

기본 필터:

```text
연도
경기 구분
팀
포지션
규정타석 또는 규정이닝
```

상세 필터:

```text
기간
직접 지정 시작일·종료일
최근 7·14·30·60·90일
최근 5·10·20·30경기
상대팀
홈·원정
요일
구장
```

필터는 관계형 SQLite 쿼리에 전달되며 DB 안의 JSON을 다시 읽지 않습니다.

## UI 분리

기존의 JSON 적재·진단·원본 확인 화면은 삭제하지 않고 `데이터 관리` 별도 창으로 분리했습니다. 기본 시작 화면은 기록실이며, 데이터 적재가 필요할 때만 기존 관리 창을 엽니다.

## 관계형 DB 및 조회

추가 화면은 다음 테이블에서 직접 조회합니다.

```text
Games
Players
GamePlayers
BatterGameStats
PitcherGameStats
PlateAppearances
Pitches
LeagueConstants
ParkFactors
ComputedCache
```

새로운 원본 JSON 저장 열은 추가하지 않았습니다. 기존 V2 관계형 DB는 그대로 사용할 수 있으며, 프로그램 시작 시 필요한 조회 인덱스를 자동 생성합니다.

추가 인덱스에는 타자·팀별 타구 유형, 타구 방향 및 구종 조회 경로가 포함됩니다.

## 웹 포팅 준비

`NaverRelay.Application`에 기록실 전용 계약 및 DTO를 추가했고, `NaverRelay.Infrastructure.Sqlite`에 DB 구현을 분리했습니다. ASP.NET Core API에 기록실 조회 엔드포인트도 추가했습니다.

```http
GET /api/record-room/batters/{view}
```

지원 view 예:

```text
basic
advanced
value
extended
clutch
power
team-batting
steal
baserunning
batted-ball
direction
discipline
pitch-types
```

## 검증

정적 검증 결과:

```text
C# 파일                         65개
프로젝트                         6개
객체 초기화 속성 검사         1,971건 PASS
enum 참조 검사                  451건 PASS
SQLite 테이블                    22개
SQLite 인덱스                    68개
기록실 UI 검사                   PASS
관계형 DB 검사                   PASS
팀 페이지 검사                   PASS
전체 V2 정적 검증                PASS
ZIP 무결성                       PASS
```

검증 명령:

```text
python validation/run_all_v2_checks.py
```

## 남은 확인 사항

현재 작업 환경에는 .NET SDK, MSBuild 및 Windows Forms 런타임이 없어 실제 C# 컴파일과 Windows 화면 실행은 수행하지 못했습니다. 사용자 PC에서 다음 순서로 최종 확인해야 합니다.

```text
NaverSabermetrics.V2.sln 열기
→ NuGet 패키지 복원
→ NaverRelay.Gui를 시작 프로젝트로 설정
→ 솔루션 다시 빌드
→ F5
```

투수 하위 탭은 현재 기본·심화·가치·투구의 임시 구조입니다. 추후 투수 기록실 스크린샷이 제공되면 같은 구조에서 세부 탭과 열을 확장할 수 있습니다.
