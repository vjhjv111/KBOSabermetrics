# KBO Sabermetrics Web — WAR v3 기준

## KBO 공식 선수 사진·프로필 (2026-09-12)

개인 페이지에 공식 사진, 등번호, 생년월일, 포지션·투타, 신체정보, 경력, 입단 계약금·연봉·지명 정보를 표시할 수 있습니다. `WEB/tools/sync_player_profiles.py`로 기존 SQLite 선수 ID에 연결해 별도 테이블과 사진 폴더에 수집합니다. 경기 JSON 파싱과 웹 조회에서 외부 수집을 실행하지 않습니다. [수집·DB 구조·사진 폴더 배포 안내](docs/PLAYER-PROFILES.md)를 참고하세요.

## 타자 비교실·구속별 대응 (2026-09-12)

상단 **비교실**에서 리그 산점도, 사진과 함께 최대 4명 성적·백분위 비교, 유사 타격 유형을 조회합니다. 분석실의 **구속별 대응**은 실제 마지막 투구의 구속으로 타격 결과를 구분합니다. [참고 사이트 조사·계산식·제외 기준](docs/COMPARISON-LAB.md)을 확인하세요.

## 분석실 (2026-09-12)

상단 **분석실**에서 공략 지도·볼배합·최근 변화·투구수/재대면·불펜 사용량·기대득점/주루·투구 재현·기록 대조·구속별 대응을 조회합니다. 기존 SQLite 사본을 읽기 전용으로 사용합니다. 2026년 정규시즌 **622경기·191,466투구** 사본으로 백엔드 빌드와 실제 데이터·경계 사례 검증을 통과했습니다.

계산식, 누락 처리, 검증 명령과 화면 QA 진행 상태는 [분석실 사용·개발 안내](docs/ANALYSIS-LAB.md), DBMS와 AWS/GCP 이전 설계는 [DB·클라우드 이전 제안](docs/DATABASE-CLOUD-PLAN.md)을 참고하세요.

## DIAMOND 야구게임 통합 (2026-09-12)

상단 **야구게임** 탭에서 타자·투수 시점의 AI/2인 대결을 실행합니다. 기록실 DB의 시즌별 실제 선수를 팀·이름으로 선택하며, 선택 당시 성적·구종·구속을 경기 판정에 사용합니다. 기존 ASP.NET Core 서버에 게임 판정과 API를 통합했으며 별도 Node 서버는 필요하지 않습니다. 게임 상태와 선수 기록 사본은 기록실 DB와 분리한 `diamond_game.db`에 저장합니다.

운영·배포 방법은 [DIAMOND 게임 운영](docs/DIAMOND-GAME.md), 소스 수정과 번들 재생성은 [게임 README](diamond-game/README.md)를 참고하세요. 일반 .NET 배포에는 저장소에 포함된 게임 번들을 사용합니다.

## wwwroot 시작 오류 수정 (2026-09-10)

서버 시작 시 소스 경로의 `wwwroot`를 찾지 못하는 문제를 수정했습니다.
전체 변경·임시 해결 명령은 `docs/WWWROOT_FIX.md`를 확인하세요.
프런트 편집은 기존대로 `frontend/`에서 하고, 프로젝트 `wwwroot` 사본은 빌드가 동기화합니다.
`verify-web.bat`은 이제 DB 검사 전에 정적 파일·매니페스트 경로를 확인합니다.

## 상태와 범위

이 패키지는 `NaverSabermetrics_V2_KboPitcherWarV3.zip`에 실제 들어 있는 Parser/Application/SQLite 계산 구현을 웹에 연결한 **소스 및 검증용 패키지**입니다. 기존 WinForms 프로그램을 덮어쓰지 않습니다.

**초기 ZIP 제작 당시 검증 메모 — 보관 기록(2026-09-12 구분):** 당시 제작 환경에서 확인한 것은 프로젝트 참조, 원본 대비 변경 범위, SQLite DDL/일부 SQL 준비, JavaScript 문법이었습니다. 당시에는 `.NET SDK`와 브라우저 실행 파일이 없고 외부 다운로드 DNS 연결도 실패하여 C# 빌드·서버·브라우저·실제 사용자 DB 실행을 검증하지 못했습니다. `docs/OFFLINE_VALIDATION.json`은 그 시점의 기록이며 현재 전체 기능의 미검증 상태를 뜻하지 않습니다. 이후 분석실의 빌드·실제 데이터·fixture 검증 결과와 화면 QA 상태는 [분석실 안내](docs/ANALYSIS-LAB.md)의 실행 검증 항목에 구분해 기록합니다.

`verify-web.bat`은 사용자의 PC에서 실제 복원·빌드·샘플 DB 적재·서버 HTTP 검사를 수행하도록 작성한 스크립트입니다. 파일이 들어 있다는 것만으로 해당 검사가 이미 통과한 것은 아닙니다.

## 가장 먼저 할 일

.NET 8 **SDK**가 있는 Windows PC에서 새 폴더에 압축을 풀고 다음 파일을 실행합니다.

```text
verify-web.bat
```

이 명령은 실제 DB가 없어도 동작하도록 포함된 7경기 JSON 샘플 ZIP으로 새 테스트 DB를 만듭니다. 의존 패키지 복원에는 인터넷 접속이 필요합니다. 첫 복원/빌드/샘플 집계 시간은 PC에 따라 다릅니다.

성공한 경우 마지막에 다음 문자열이 나와야 합니다.

```text
ALL_EXECUTED_CHECKS_PASS
```

실패하면 그 단계에서 중단합니다. 성공·실패 모두 다음 위치에 로그를 남깁니다.

```text
artifacts/verify-날짜-식별자/logs/
artifacts/verify-날짜-식별자/verification-logs.zip
```

로그 ZIP에는 DB, 비밀번호, 로컬 설정 파일, 쿠키 암호화 키를 넣지 않습니다. 단, 오류 메시지에 로컬 경로·SQL·선수 코드 등이 포함될 수 있으니 외부 전달 전 확인하세요.

## 검증이 하는 일

| 순서 | 검사 | 성공 기준 |
|---|---|---|
| 1 | .NET SDK 정보, NuGet 복원, Release 빌드 | 각 명령 종료 코드 0 |
| 2 | 새 샘플 DB에 7경기 적재 | 업로드 소스의 KnownSampleValidator 기준값 일치 |
| 3 | 테이블/컬럼, quick_check, foreign_key_check | 누락·손상·FK 위반 없음 |
| 4 | 기존 집계 경로 ↔ 웹의 역할별 집계 경로 비교 | 동일 DB·조건에서 기록/세이버/WAR 결과 일치 |
| 5 | WAR 목표 산술, 이닝 입력, 시간 제한, 일일 한도 저장 | 코드에 정의된 기대 결과 일치 |
| 6 | 임시 계정으로 실제 루프백 서버 접속 | 로그인·CSRF·API·27개 기록 탭 응답 정상 |
| 7 | 비로그인/위조 요청/잘못된 정렬/과다 결과 요청 | 401/403/400 등 기대 거부 응답 |
| 8 | 별도 낮은 속도 제한으로 서버 재실행 | 연속 요청에 429 반환 |
| 9 | HTTP 검사 전후 샘플 DB 파일 해시 | 읽기 전용 원본 바이트 불변 |

`--compare`는 **포팅 과정에서 계산값이 바뀌지 않았는지** 검사합니다. FanGraphs/Statiz 공식 기록과의 일치, 구종 분류 정확성, 데이터 누락, 원본 WAR 정책의 타당성을 독립적으로 인증하는 검사는 아닙니다.

## 샘플 화면 실행

검증 후 화면을 먼저 보고 싶을 때:

```text
start-sample.bat
```

로그인 ID와 12자 이상 비밀번호를 입력합니다. 원문 비밀번호 대신 해시를 `local-settings.json`에 저장합니다. 안내된 주소를 브라우저에 입력합니다.

```text
http://127.0.0.1:5080
```

로그인 후 팀/연도/탭을 고르고 `조회`를 누릅니다. 자동 테스트가 실제 서버의 HTML 응답은 확인하지만, 화면 배치와 클릭 동선은 이 단계에서 눈으로 확인해야 합니다. 샘플은 전체 시즌 데이터가 아니므로 연간 순위나 WAR 규모를 실전 결과로 해석하지 마세요.

## 실제 DB 연결

데스크톱 프로그램을 종료한 뒤 아래 파일을 실행하세요. 원본을 수정하지 않는 방식이지만, 복사 시 쓰기 작업과의 경합을 피하려고 종료를 권합니다.

```text
start-local.bat
```

원본 DB 경로를 입력하고 계정을 등록합니다. 원본 경로 예시:

```text
C:\Users\사용자명\AppData\Local\NaverSabermetrics\Data\sabermetrics_v2.db
```

내부 절차:

```text
원본 스키마 검사(읽기 전용)
→ SQLite BackupDatabase로 NEW 파일 생성
→ 새 복사본에서만 V3 인덱스/리그 계산 캐시/통계 준비
→ 웹 서버는 완성된 복사본을 읽기 전용으로 조회
```

원본 JSON 재파싱은 하지 않습니다. 기존 DB 및 기존 출력 파일을 덮어쓰지 않습니다. 8GB 원본이면 **복사본과 임시 작업 공간을 위한 여유 디스크 공간**이 필요합니다. 첫 복사·ANALYZE·리그 보정 캐시 생성은 오래 걸릴 수 있으며 평상시 조회 시간과 구분해야 합니다. 복사 실패 시 임시 복사본만 정리합니다.

이미 `start-sample.bat`을 실행하여 로컬 설정이 생겼다면 `start-local.bat`만으로는 그 샘플 설정을 재사용합니다. 실제 DB로 변경할 때는 PowerShell에서 명시적으로 경로를 줍니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\start-local.ps1 `
  -DatabasePath 'C:\Users\사용자명\AppData\Local\NaverSabermetrics\Data\sabermetrics_v2.db'
```

실행 중인 웹 복사본 파일을 교체하지 마세요. 새 경기를 반영할 때는 새 복사본을 준비한 다음 서버를 중지하고 설정 경로를 바꿔 재시작합니다. 이 패키지는 실시간 쓰기 DB를 여러 프로세스에 공유하는 배포 구성이 아닙니다.

## 실제 DB 결과 대조

새 복사본이 준비된 다음:

```text
verify-real-db.bat
```

`local-settings.json`의 복사본 DB를 읽어 스키마·무결성·원본 집계 대조를 수행합니다. 실행 로그는 `artifacts/real-db-날짜/`에 저장합니다. 큰 DB를 대상으로 하는 전체 통산 검사/무결성 검사는 오래 걸릴 수 있습니다.

그다음 브라우저와 데스크톱을 다음처럼 동일한 조건으로 맞춰 수동 확인합니다.

- 같은 원본의 동일 시점 복사본, 연도, 정규시즌, 팀, 기간, 규정 비율, 조건1/2를 사용합니다.
- 선수명 대신 `pcode + 팀 + 집계 범위`로 행을 대조합니다. 시즌 중 이적한 선수의 팀별/통합 행을 섞지 않습니다.
- 타자는 PA/AB/H/HR/BB/SO/AVG/OPS/wRC+, 투수는 IP/ER/SO/ifFIP/최종 KBO fWAR/RA9-WAR/Blend WAR를 확인합니다.
- 반올림된 UI 값과 내부 원정밀도는 구분합니다. 웹 숫자 자릿수 차이가 수학적 차이는 아닙니다.
- 상황 필터에서는 정확히 분리할 수 없는 최종 ER/IP/WAR를 웹에서 `-`로 숨기거나 요청을 거부합니다. 이것을 0점으로 읽지 않습니다.

## 속도 확인

일반 시즌 조회, `2아웃 + 득점권`, `1-1 카운트 + 구종` 등 자주 쓰는 조건을 정해 첫 조회와 반복 조회를 각각 기록하세요. 동일한 조건을 반복했을 때 화면에 캐시 사용 여부가 표시됩니다. 서버 로그는 `elapsedMs`, 행 수, 캐시 여부, 요청 ID를 남기며 브라우저 개발자 도구 Network는 전체 왕복 시간을 보여줍니다.

최초 리그 계산·새 스냅샷 생성 시간과 일반 HTTP 조회를 섞지 않습니다. 현재는 실제 8GB DB 실측이 없으므로 “몇 배 빨라짐”, “0.1초” 같은 수치를 보장하지 않습니다. 필터를 바꿀 때마다 즉시 무거운 SQL을 반복하지 않고 조회 버튼으로 실행합니다. 서버 동시 조회 기본 2개, 제한시간 30초이며 원시 이벤트를 브라우저로 대량 전송하지 않습니다.

## 서버/프런트 구조

```text
frontend/                             HTML/CSS/JS (통계 수학 없음)
server/src/NaverSabermetrics.Web/      인증, 쿼리, 정렬, 포맷, 페이지, 검증 CLI
server/src/NaverRelay.Application/     업로드 V3의 DTO/계약/공식 팩토리
server/src/NaverRelay.Infrastructure.Sqlite/  업로드 V3 + 웹 읽기 전용 어댑터
server/src/NaverRelay.Parser/          CLI 샘플 적재/기존 모델, HTTP import 없음
```

`NaverSabermetrics.Web.sln`은 WinForms/예전 공개 API 프로젝트를 참조하지 않습니다. Visual Studio로 열 때 시작 프로젝트는 `NaverSabermetrics.Web`입니다. 설정이 없는 첫 실행은 거부되므로, 먼저 위 배치 파일로 샘플 또는 복사본/계정을 준비하세요.

프런트는 종전 기록실의 시즌/통산/팀/상수, 타자13/투수12 탭, 상세필터, 서버 정렬/페이징을 유지합니다. 선수 검색 후 해당 선수의 기록실 행을 좁혀 볼 수 있습니다. **WinForms의 선수 개인 대시보드 전체를 웹으로 옮긴 버전은 아닙니다.**

## WAR v3 연결 기준

- 이번에 실제 업로드된 ZIP의 SHA-256은 `docs/source-baseline-manifest.json`에 기록했습니다.
- `KboPitcherWarMath.cs`, `DatabaseCacheService.PitcherWarCalibration.cs`는 업로드 파일과 바이트 단위로 동일합니다.
- 최초 웹 포팅에서는 `DatabaseAnalyticsService.cs`의 partial 선언만 변경했습니다. 이후 K/9·BB/9·HR/9는 공식 경기 집계가 있을 때 공식 기록과 아웃 수를 사용하도록 보정했습니다. 이닝은 항상 `아웃 수 / 3.0`으로 환산합니다.
- DB 원본 저장/읽기 계층 변경3개와 신규 웹 어댑터를 분리했습니다.
- 업로드 원본의 리그 기준/표본 선택 정책을 그대로 사용합니다. 과거 설명 문장과 실제 코드가 다르더라도 이 포팅에서 몰래 교정하지 않습니다.
- 이 연결 버전의 표시 식별자는 `Uploaded-KboPitcherWarV3-web.1`이며 WAR 숨김 기본값을 해제했습니다.

## 보안과 공개 배포 전 조건

계정은 관리자가 설정하는 방식이고 공개 회원가입은 없습니다. HttpOnly 세션 쿠키, CSRF, 요청 속도/일일 열람 한도, 허용 스탯 정렬/필터, 결과 상한, 원본 다운로드 차단을 적용했습니다. **화면에 전달된 값의 수집 자체를 완전히 금지할 수는 없습니다.** 정상 로그인한 자동화도 브라우저와 같은 결과를 받을 수 있습니다. 이 기능은 대량 수집 비용/속도를 제한합니다.

`start-local`은 로컬 HTTP 확인용 Development 모드입니다. 그대로 외부에 포트 개방하지 마세요. 운영은 HTTPS, 명시적 프록시 신뢰 목록/도메인 AllowedHosts, 계정/비밀번호 정책, 상태 디렉터리 접근권한, 키 백업, 보안 검토가 필요합니다. 로그인 쿠키 키를 디스크에 유지하지만 저장 암호화/ACL은 운영 환경에서 별도 관리해야 합니다.

일일 한도는 계정과 IP에 동시에 적용하며 UTC 날짜 기준입니다. 동일 공유 IP의 사용자가 한도를 공유할 수 있습니다. 요청 속도 제한은 현재 서버 프로세스 단위이므로 다중 인스턴스 배포 시 분산 제한 설계가 추가로 필요합니다. 설정/DB 경로/로그/키를 `wwwroot` 안에 넣지 마세요.

Dockerfile은 실행 구성 예시이며 이 환경에서 이미지 빌드/운영 배포를 검증하지 않았습니다.

## 참고한 공식 문서

- Microsoft.Data.Sqlite BackupDatabase: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup
- Microsoft.Data.Sqlite 비동기 처리 한계: https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
- SQLite 쿼리 실행계획: https://www.sqlite.org/eqp.html

백업은 실행 중인 DB에도 사용할 수 있으나 BackupDatabase 실행 동안 다른 연결의 쓰기를 막을 수 있습니다. 따라서 크롤러/데스크톱 적재를 쉬는 시간에 복사하는 편이 안전합니다.
