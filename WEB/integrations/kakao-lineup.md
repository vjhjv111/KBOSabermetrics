# 카톡 선발 라인업

- `/라인업 한화`, `/라인업 @한화`, `/라인업 한화이글스`: 한국 날짜 기준 오늘 선발 1~9번과 포지션, 선발투수.
- `GET /api/bot/lineup?team=한화`: `text`를 그대로 답장한다. JSON에는 경기 ID, 상대, 상태, 선수 배열, 수집 시각도 포함된다.
- 기존 `BotStartersWorker`가 오늘/내일 일정을 조회하면서 네이버 `/schedule/games/{id}/preview`의 `previewData.homeTeamLineUp/awayTeamLineUp.fullLineUp`을 저장한다. 선발투수는 기존 game-polling을 사용한다. 정상 수집 시 시작 간격 5분이며, 한 사이클이 5분을 넘으면 중첩하지 않고 완료 후 재시도한다.
- `Site:StateDirectory/bot-starters.json`에 보관한다. 기존 선발투수 파일은 그대로 호환된다. Render에서는 기존 선발 수집기 활성화 조건을 따른다. 로컬 테스트는 `BotStarters__Enabled=true`로 켠다.
- API는 캐시만 읽는다. 통계 DB 편입이나 경기 종료를 기다리지 않는다. 기존 봇 API와 같은 요청 제한을 적용한다.
- `fullLineUp`에서 타순 1~9번만 보관하고 투수·후보는 제외한다. 경기 전 수정은 반영하며 경기 시작 후에는 확보된 선발 명단과 포지션을 고정한다. 고정 여부도 파일에 저장하여 재시작 후 유지한다. 양 팀 명단 확보·고정 후에는 preview 요청을 생략한다.
- 네이버 경기 전 데이터가 없는 경우 미발표로 안내한다. 발표 시점은 네이버 데이터 제공 시점에 따른다. 경기 시작 후 처음 실행된 경우에도 중계의 현재 명단으로 대체하지 않고 preview의 원래 선발만 사용한다. 완전한 9명을 확인할 수 없으면 확인 불가로 안내한다.
- 휴식일, 취소, 더블헤더, 일정에서 사라진 경기, 수집 실패·15분 이상 지연을 구분한다.
- 봇의 기존 `KBO_SITE_BASE_URL`, `kboHttpGet`를 사용한다. `kakao-lineup.js`의 함수를 추가하고 주석의 분기문을 `responseFix` 시작에 넣는다.
- 개인 설정이 포함된 전체 봇 파일은 Git에 넣지 않는다.

검증: `dotnet run --project WEB/validation/BotLineups/BotLineups.Validation.csproj` 및 `node WEB/validation/BotLineups/kakao-lineup.test.cjs [전체봇파일]`.
