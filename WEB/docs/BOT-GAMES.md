# 카톡봇 `/경기`

`GET /api/bot/games`는 한국시간 오늘의 경기 현황을 반환합니다.
`GET /api/bot/games?date=2026-09-23`처럼 날짜를 지정할 수도 있습니다.
잘못된 날짜는 400, DB 미준비는 503, 조회 제한은 기존 봇 API 정책을 따릅니다.
쿠키·CSRF 토큰 없이 GET으로 호출합니다.

응답 필드:

- `date`, `timeZone`: 조회 날짜와 `Asia/Seoul`.
- `games`: `id`, `date`, `time`, `stadium`, `status`, `statusText`, `away`, `awayName`, `home`, `homeName`, `awayScore`, `homeScore`, `awayPitcher`, `homePitcher`, `decisions`, `updatedAt`, `finished`.
- `updatedAt`: 응답 경기 중 가장 최근 JSON 수집 시각. DB 기록만 있으면 null. 개별 경기 시각은 각 `games[].updatedAt` 참조.
- `hasLiveGames`, `refreshSeconds`: 진행 경기 존재 여부와 수집 목표 간격(60초). 봇이 자동 메시지를 발송하라는 의미는 아닙니다.
- `text`: 카톡 답장에 그대로 사용할 문자열. 점수·상태·현재 투수 또는 종료 투수 결정을 표시합니다.

현재 수집 구간에서는 홈과 같은 JSON 투영을 사용하며, 종료된 DB 기록이 있으면 이를 우선합니다. 순위·WAR를 재계산하지 않고 봇 조회마다 외부 사이트를 요청하지 않습니다. 경기·일정이 없으면 해당 날짜에 수집된 자료가 없다고 알리고 이전 날짜 경기로 바꾸지 않습니다. 진행 상태에서 마지막 수집이 3분보다 오래됐으면 지연 안내를 붙입니다.

## AIbotUDPToPC 연동

`WEB/integrations/kakao-games.js`의 함수를 기존 봇에 추가하고, 파일 하단 주석의 `/경기` 분기를 `responseFix` 앞부분에 넣습니다. 기존 `KBO_SITE_BASE_URL`과 `kboHttpGet`을 그대로 사용합니다. `/경기`는 날짜를 생략해 서버가 한국시간을 적용하고, `/경기 YYYY-MM-DD`는 지정 날짜를 요청합니다.

서버를 먼저 배포한 뒤 수정된 봇 JS를 교체·저장·재컴파일합니다. 다른 스크립트가 `/경기`를 처리한다면 그 명령 분기를 비활성화해 중복 답장을 방지합니다. `/순위`, `/기록`, `/월간일정`, PC 연결 명령 등은 변경하지 않습니다.

검증:

```text
dotnet run --project WEB/validation/HomeLive/HomeLive.Validation.csproj
node WEB/validation/HomeLive/kakao-games.test.cjs [수정한 전체 AIbotUDPToPC.js 경로]
```

JS 검증은 Java 스레드와 HTTP를 모킹하며 실제 카톡 메시지를 발송하지 않습니다.
