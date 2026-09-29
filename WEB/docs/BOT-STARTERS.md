# 카카오톡 선발 명령

- `/선발`: 한국시간 오늘 경기 선발투수.
- `/내일선발`: 한국시간 내일 경기 예고 선발투수.
- `GET /api/bot/starters?dayOffset=0` (기본 0), 내일은 1. 다른 값은 400.
- 응답: date, timeZone, updatedAt, available, stale, refreshSeconds, games, text.
- 봇은 text를 그대로 답장한다. 날짜 계산은 서버가 담당한다.
- text는 `한화 박준영 vs 페덱 삼성`처럼 경기당 한 줄이다. 제목·경기 시각·수집 시각·상시 안내 문구는 생략하고 수집 지연/실패 안내만 필요할 때 표시한다. 구조화된 JSON 필드는 유지한다.

`local/war-blend-lineup-20260929`의 ProbableStarterFetcher에서 선발 조회 부분만 분리했다. 전송/파싱 실패를 미발표로 처리하지 않도록 보완했다. WAR·라인업 최적화 변경은 포함하지 않는다.

수집기는 시작 직후 오늘과 내일 일정을 수집하고, 한 주기 완료 후 5분 뒤 다시 실행한다. 네이버 game-polling의 awayStarterName/homeStarterName만 읽으며 현재 구원투수로 대체하지 않는다. 일정 조회 실패 시 기존 날짜 스냅샷을 유지한다. 개별 경기 조회 실패 시 이전 선발과 그 수집 시각을 유지하고 FetchFailed를 표시한다. 정상 응답에서 선발이 비어 있으면 미발표로 표시한다. 취소 경기에는 선발을 표시하지 않는다. 15분 이상 지난 스냅샷이나 일부 조회 실패에는 지연 안내가 붙는다.

캐시는 `Site:StateDirectory/bot-starters.json` (Render 기본 `/var/data/state/bot-starters.json`)에 원자적으로 교체 저장한다. 시즌 통계 DB는 수정하지 않는다. 봇 API 호출은 외부 사이트 요청을 발생시키지 않는다.

Render에서 기존 수집기가 활성화돼 있으면 자동 실행한다. `BotStarters__Enabled=true/false`로 별도 제어할 수 있다. 로컬 기본값은 꺼짐이며 검증 시 `--BotStarters:Enabled true`로 켠다.

서버 배포 후 봇 전체 교체본을 설치하거나 `WEB/integrations/kakao-starters.js`의 함수 및 responseFix 분기를 기존 봇에 추가한다. 기존 `/경기`와 다른 명령은 유지한다. 별도 봇에 같은 명령이 있으면 중복 응답을 방지하도록 정리한다.
