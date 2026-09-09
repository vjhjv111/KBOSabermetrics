# 2.6 KBO Pitcher WAR v3

- KBO 최근 3시즌 저사용 투수 표본 기반 선발·구원 대체 pFIPR9/pRA9 추가
- 역할별 Replacement FIP- 공개
- 리그 목표 투수 WAR와 WARIP 재중앙화 추가
- KBO fWAR v3, KBO RA9-WAR, Blend WAR 70/30 추가
- 선수·팀·투수 기록실과 연도별 상수 화면에 새 WAR 지표 연결
- 공식·계산기 탭에 대체수준, WARIP, fWAR, RA9-WAR, Blend WAR 공식 추가
- 리그 상수 캐시 및 통계 캐시 버전 갱신

## V2 Pitcher Record Room Update
- 투수 기록실 탭을 기본/심화/가치/확장/WP/주자/선발/구원/타구/타구방향/투구/구종으로 재구성
- 투수 기본·심화·역할별 가치 DTO와 화면 포맷 추가
- 원본 WPA 기반 pLI/gmLI/WPA 조회 추가
- 선발 QS/QS+, 구원 연투·gmLI, 주자 허용, 타구·방향·존 지표 추가
- 구종별 가치/100구/구속/구사율/투구수/피AVG/피SLG 피벗 추가
- `/api/record-room/pitchers/{view}` 웹 API 추가
- 투수 타구·방향·존·구종 관계형 SQLite 인덱스 보강
- 연봉·정밀 수비·확정할 수 없는 의사결정 기록은 제외

## V2 Team Pages Update
- 메인 도구모음 및 Ctrl+T 팀 페이지 추가
- 연도별 팀 타격·투구·Value·WAR 추가
- 팀 소속 선수별 타격·투구 순위와 선수 페이지 연결
- 상대 팀별 승패·득실차·타격·투구 기록 추가
- 주자/아웃/이닝/점수차/득점권/클러치/장소 상황별 기록 추가
- 팀 구종별 타격 AVG/OBP/SLG/OPS/wOBA와 투구 성과 추가
- `/api/teams`, `/api/teams/{teamCode}` 웹 API 추가
- 팀 단위 관계형 조회 인덱스와 숫자 표시 형식 보강

## V2 Player Splits Update
- 선수 역할별 탭 자동 표시
- 상대별/상황별/구종별 분석 추가
- 상황/구종 조회 인덱스 추가

# Changelog

## Naver Sabermetrics V2 — 2026-08-06

- 솔루션을 Parser / Application / Infrastructure.Sqlite / Gui / Api / Cli 6개 프로젝트로 재구성
- 새 관계형 DB `%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_v2.db` 사용
- 기존 `sabermetrics.db`, `sabermetrics_warehouse.db`와 완전 분리
- 경기 전체 JSON blob 저장·재역직렬화 경로 제거
- 원본 JSON은 신규·변경 경기 import에서만 한 번 사용
- 모든 통계·선수 검색·개인 페이지·로그를 관계형 SQLite 조회로 변경
- SQLite 패키지 참조를 Infrastructure 프로젝트로 격리
- WinForms와 ASP.NET Core API가 같은 Application 계약과 DB 구현을 사용하도록 구성
- `/api/health`, `/api/catalog`, `/api/games`, 타자/투수 통계, 선수 검색/상세 API 추가
- `roundCode == "kbo_r"` 정규시즌 기준을 SQL과 리그 환경 계산에 고정
- 기존 CS0173 nullable 조건식, CS1628 out 매개변수 캡처, Application 네임스페이스 충돌 방지
- `verify-v2.bat`, V2 아키텍처·빌드·스모크 테스트 문서 추가
- 52개 C# 파일, 6개 프로젝트, SQLite 스키마·SQL·의존 방향 정적 검증 통과

## SQLite 관계형 통계 데이터웨어하우스

- 새 DB 파일 `%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_warehouse.db` 사용
- 기존 대형 `Games.NormalizedJson` 저장 및 조회 제거
- 원본 JSON은 신규·변경 경기 import에서만 한 번 사용
- Games, Players, PlateAppearances, Pitches, RunnerEvents, PlayerChanges 등 관계형 테이블 적재
- 선수·팀·경기 1행 구조의 BatterGameStats/PitcherGameStats 추가
- 시즌·기간·최근 N경기·선수 페이지를 관계형 SQL 집계로 변경
- 필터별 최종 AnalyticsSnapshot을 DataVersion 기반 ComputedCache에 영구 저장
- 동일 GameId 재가져오기 시 하위 관계 행을 트랜잭션으로 교체
- ParsedSources 지문을 통한 신규·변경 문서 증분 가져오기 유지
- 모든 정규시즌/리그 상수/파크 팩터를 `roundCode == "kbo_r"`로 제한
- SQLite DB 상태·경로·크기·최적화 탭 추가
- 사용되지 않던 전체 NormalizedGame 메모리 기반 통계/선수 서비스 제거
- 웹 포팅을 위한 Parser/Application/Repository/UI 경계 유지

## Phase 1

- 원본 DTO 전체 nullable 처리 및 확장 필드 보존
- `lastValidMetricOption`, 구조화된 `playerChange` 모델링
- 릴레이 그룹과 공식 타석 분리
- `type=13/23` 타자 결과, `type=14/24` 주자 결과 분리
- 실제 투구 시점의 투수 배정
- 홈·원정 공격 플래그 수정
- 중복 가능한 `seqno` 대신 복합 내부 ID 생성
- 타순 슬롯 기반 주자 식별 상태 머신 추가
- 피치클락·비디오 판독·마운드 방문·퇴장 분리
- PTS 홈플레이트 통과 높이 계산
- 경기 최종 타격 라인 `PA/AB/H/HR/BB/HBP/SO` 교차 검증 및 진단 추가
- 미분류 릴레이 그룹 진단과 검증 요약 카운터 추가
- JSON·폴더·ZIP 일괄 처리 CLI 추가
- 7경기 알려진 샘플 검증 추가

## GUI 패키지

- .NET 8 WinForms `NaverRelay.Gui` 프로젝트 추가
- Visual Studio 솔루션을 `NaverSabermetrics.sln`으로 구성
- JSON/ZIP/폴더 입력 검색 및 선택 파싱 추가
- 진행률, 취소, 로그, 원본 JSON 미리보기 추가
- 경기/타자/투수/타석/투구/주루/교체/관리/진단 탭 추가
- 정규화 JSON 저장과 현재 표 CSV 내보내기 추가
- 제공된 7경기 샘플과 GUI 자동 기준 검증 추가
- PostgreSQL 탭은 2단계 스키마 확정 전까지 명시적으로 비활성화

## Value / WAR / Formula

- 타자 Value·WAR 탭 추가
- 투수 Value·WAR 탭 추가
- Site WAR v1 및 Pitching WAR v1 계산 추가
- 공식·설명 탭 추가
- wOBA/FIP 계산기 추가
- 리그 상수에 WAR 임시 상수 표시

## Column sorting
- 모든 DataGridView 열 머리글 클릭 정렬 지원
- 첫 클릭 오름차순, 두 번째 클릭 내림차순
- 숫자는 숫자값으로, 문자는 문화권 기준으로 정렬
- null 값은 정렬 방향과 무관하게 목록 아래쪽에 배치

## Year filter / global league constants

- 읽어온 `SeasonYear` 기반 연도 드롭다운 추가
- 연도와 경기 구분 복합 필터 적용
- 리그 상수와 세이버 기준 환경은 필터와 무관하게 읽어온 전체 데이터에서 계산

## FanGraphs형 투수 WAR v2
- 원본 경기별 투수 최종 ER/R/IP 사용
- 지정된 6개 내야 뜬공 문구를 IFFB로 집계
- ifFIP, FIPR9, 구장 보정 pFIPR9, 동적 dRPW 추가
- 선발/구원 대체수준 분리
- 원본 WPA 기반 gmLI 및 LI 배수 추가
- 다년 JSON 자동 집계 파크 팩터 탭 추가

## 기간 기반 세이버매트릭스 필터
- 직접 지정 시작일/종료일
- 최근 7/14/30/60/90일
- 전반기/후반기
- 최근 5/10/20/30경기
- 상대팀, 홈/원정, 요일, 구장 필터
- 필터된 경기 데이터를 기준으로 클래식/세이버/wRC+/WAR/FIP/ifFIP를 전부 재집계
- 리그 상수는 기존 정책대로 전체 kbo_r 데이터 기준 유지

## SQLite 영구 캐시 / 증분 파싱
- 파싱 결과를 `%LOCALAPPDATA%\\NaverSabermetrics\\Data\\sabermetrics.db`에 자동 저장
- 재실행 시 DB에서 정규화 경기 자동 복원
- 원본 문서 지문을 비교하여 변경 없는 JSON/ZIP 엔트리 자동 건너뛰기
- 신규/변경 경기만 파싱하고 `GameId` 기준 upsert

## 선수 검색·개인 페이지 / Web-ready 계약

- `NaverRelay.Application` 프로젝트 추가
- WinForms·SQLite·HTTP와 독립적인 `IPlayerPageService` 및 선수 페이지 DTO 추가
- 상단 선수 이름/선수 코드 검색과 `Ctrl+F` 단축키 추가
- 동명이인 선택 시 선수 코드·생년월일·최근 팀·포지션·역할 표시
- 타자/투수/세이버/WAR 표 행 더블클릭으로 개인 페이지 열기
- 정규시즌 연도·팀별 타격 및 투구 스탯 추가
- Value·WAR, 경기 로그, 타석 로그, 투구 로그 추가
- Rolling 7/15/30경기 wRC+ 표와 그래프 추가
- 선수별 공식·최근 시즌 계산 근거 화면 추가
- 원본 최종 라인의 생년월일·키·몸무게·등번호·투타 정보를 정규화 모델에 보존
- 선수 식별은 이름이 아니라 `pcode`를 기본키로 사용
- SQLite 파서 캐시 버전을 `player-profile-v2`로 올려 기존 원본을 1회 재파싱한 뒤 다시 증분 캐시 적용
- 향후 ASP.NET Core API에서 같은 선수 페이지 응답 계약을 사용할 수 있도록 계층 경계 마련

## SQLite DB 중심 지연 로딩 / 메모리 안정화

- 프로그램 시작 시 `Games.NormalizedJson` 전체 역직렬화 제거
- `_games` 전역 전체 경기 목록 제거
- 시작 시 경기 수·연도·팀·구장 등 SQLite 메타데이터만 조회
- 기본 화면을 DB의 최신 연도 경기 헤더 조회로 변경
- 구버전 DB 메타데이터·선수 인덱스를 8경기 단위로 보강하는 재개 가능한 1회 마이그레이션 추가
- `Players` 및 `GamePlayers` 인덱스를 이용한 선수 검색과 선택 선수 전용 조회 추가
- 통계 탭을 현재 선택된 필터 범위만 한 경기씩 스트리밍 집계하도록 변경
- 동일 필터의 선수 통계 결과를 `ComputedCache`에 저장
- 리그 상수·파크 팩터를 전체 `roundCode == "kbo_r"` 데이터에서 지연 계산 및 캐시
- 타석·투구·주루·교체·관리·진단 탭을 최대 5,000행 페이지 조회로 변경
- 대규모 신규 파싱에서 저장 완료 경기 객체를 즉시 해제
- 필터·탭 변경 시 이전 DB 조회 자동 취소
- `Esc`로 파싱뿐 아니라 현재 화면 조회도 취소 가능
- 결과 JSON 저장을 선택 범위 DB 스트리밍 방식으로 변경
- `docs/SQLITE_LAZY_LOADING.md` 추가

## 2.4 Record Room UI

- 시즌기록실/통산기록실/팀기록실/연도별 상수 최상위 탐색 추가
- 타자/투수 2차 탐색 및 타자 13개 하위 기록 탭 추가
- 필터 적용 조건을 선수명 바로 오른쪽에 표시
- 클러치, 타구 유형, 타구 방향, 최종구 기준 구종 SQL 집계 추가
- 기존 MainForm을 별도 데이터 관리 창으로 전환
