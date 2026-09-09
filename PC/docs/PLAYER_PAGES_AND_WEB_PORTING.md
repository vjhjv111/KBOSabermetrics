# 선수 검색·개인 페이지와 웹 포팅

## 선수 식별

선수 기본키는 이름이 아니라 원본 `pcode`입니다.

검색 결과에는 다음 정보를 함께 표시합니다.

```text
pcode
선수명
생년월일
최근 소속팀
주 포지션
타자/투수 구분
투타
활동 연도
```

동명이인은 이 정보를 확인한 뒤 선택합니다.

## 데스크톱 사용

- 상단 검색창에 이름 또는 pcode 입력
- `Enter` 또는 `선수 페이지` 버튼
- `Ctrl+F`로 검색창 포커스
- 타자·투수·세이버·WAR 표에서 선수 행 더블클릭

## 개인 페이지

### 프로필

```text
선수명 / pcode
생년월일
최근 팀 / 팀 이력
주 포지션 / 역할 / 투타
커리어 G / PA / IP / WAR
```

### 연도별 타격

`roundCode == "kbo_r"` 경기만 사용해 연도·팀별 한 행을 만듭니다.

- G, PA, AB, R, H, 1B, 2B, 3B, HR, TB, RBI
- SB, CS, BB, IBB, HBP, SO, GDP, SH, SF
- AVG, OBP, SLG, OPS, ISO, BABIP
- BB%, K%, wOBA, wRAA, wRC, wRC+, OPS+
- 타격/주루/포지션/대체 Runs, RAR, WAR

### 연도별 투구

경기별 최종 투수 라인을 연도·팀별로 합산합니다.

- G, GS, 구원 G, IP, R, ER, H, HR, BB, HBP, SO, IFFB
- ERA, WHIP, K/9, BB/9, HR/9
- FIP, xFIP, ifFIP, FIPR9, FIP PF, pFIPR9
- dRPW, gmLI, LI 배수, FanGraphs형 투수 WAR v2

### 로그와 Rolling

- 경기 로그
- 타석 로그
- 투구 로그
- Rolling 7/15/30경기 wRC+
- 계산 근거

Rolling wRC+는 각 경기의 wRC+ 평균이 아니라 직전 N경기 타석을 합산해 다시 계산합니다.

## 관계형 DB 조회

선수 페이지는 경기 전체 JSON을 열지 않습니다.

```text
Players
→ pcode 확인
→ BatterGameStats / PitcherGameStats
→ 해당 선수 PlateAppearances / Pitches
→ 연도별·Rolling DTO
```

프로그램을 껐다 켜도 기존 관계 행을 바로 조회합니다.

## Application 계약

`NaverRelay.Application`은 WinForms, SQLite, HTTP를 참조하지 않는 선수 페이지 계약과 DTO를 제공합니다.

```csharp
public interface IPlayerPageService
{
    IReadOnlyList<PlayerSearchItem> SearchPlayers(
        string? query,
        int maxResults = 100);

    PlayerPageData? GetPlayerPage(string pcode);
}
```

SQLite 구현은 `NaverRelay.Infrastructure.Sqlite.DatabasePlayerPageService`에 있습니다.

## 현재 API

`NaverRelay.Api`가 이미 동일 DB를 조회합니다.

```http
GET /api/players/search?q=이승현
GET /api/players/51454
```

추가하기 좋은 세분화 경로:

```http
GET /api/players/51454/seasons/batting
GET /api/players/51454/seasons/pitching
GET /api/players/51454/games?year=2026
GET /api/players/51454/plate-appearances?year=2026
GET /api/players/51454/pitches?year=2026
GET /api/players/51454/rolling/wrc-plus?window=15
```

## 웹 프런트엔드 포팅

권장 구조:

```text
React 또는 Next.js
       ↓ JSON/HTTP
NaverRelay.Api
       ↓
NaverRelay.Application
       ↓
NaverRelay.Infrastructure.Sqlite
```

서버 배포 규모가 커지면 SQLite 구현과 나란히 PostgreSQL 구현을 추가합니다. 선수 페이지 DTO와 통계 공식은 유지하므로 데스크톱과 웹에서 같은 결과를 제공할 수 있습니다.
