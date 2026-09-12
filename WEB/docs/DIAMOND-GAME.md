# DIAMOND 웹게임 운영

## 사이트 연결

사이트 상단 **야구게임** 탭은 `/#diamond`로 이동합니다. 기존 사이트의 메뉴를 유지한 채 동일 출처의 `/diamond/index.html`을 iframe으로 표시합니다. 경기기록·기록실·홈 탭으로 이동하면 게임 화면을 숨기고, 다시 야구게임을 누르면 같은 화면으로 돌아옵니다.

게임 화면이 숨겨지거나 브라우저 탭이 비활성화되면 게임 조회 반복 요청과 화면 타이머를 중단하고 3D 장면을 해제합니다. 이는 서버에서 진행 중인 투구의 시간을 멈추는 기능은 아닙니다. 대결로 돌아오면 서버의 현재 상태를 다시 조회합니다.

## 서버와 저장 파일

게임의 API와 판정 엔진은 기존 `NaverSabermetrics.Web` 프로세스에서 실행합니다. 별도 서비스·포트·Node 서버는 필요하지 않습니다.

| 항목 | 경로·역할 |
| --- | --- |
| 게임 화면 | `wwwroot/diamond/` |
| 사이트 탭 연결 | `wwwroot/diamond.js` |
| 서버용 게임 자료 | 게시 폴더의 `diamond-data/` |
| 대결 상태 | `Site:StateDirectory` 아래 `diamond_game.db` |
| 기록실 원본 | 기존 `sabermetrics_v2.db`를 계속 읽기 전용으로 사용 |
| 게임 API | `GET /api/diamond/action?code=...`, `POST /api/diamond/action` |

Render에서 상태 폴더의 기본값을 사용하면 게임 DB는 `/var/data/state/diamond_game.db`에 생성됩니다. 지속 디스크와 웹 프로세스의 쓰기 권한이 필요합니다. SQLite가 실행 중이면 `diamond_game.db-wal`, `diamond_game.db-shm` 파일도 생길 수 있습니다. 이 파일들은 공개 웹 폴더나 Git 저장소에 넣지 않습니다.

게임 상태 DB와 기록실 DB는 분리됩니다. 게임 조작이 수집된 야구 기록이나 WAR를 변경하지 않습니다. 게임은 게시된 자료 파일을 사용하므로 기록실 DB가 아직 준비되지 않은 상태에서도 게임 API를 별도로 제공할 수 있습니다.

## 선수 자료의 범위

이번 통합은 기존 게임의 **2025 정규시즌 타자 20명·투수 22명**을 유지합니다. 수집 DB와 연결해서 2026 선수나 능력치를 자동 생성하는 기능은 포함하지 않습니다.

`diamond-game/lib/players.json` 등 자료 파일은 게임 화면 빌드와 C# 판정 엔진 양쪽에서 사용합니다. 선수·구종·신체 자료를 변경하면 서로 다른 자료가 배포되지 않도록 게임 번들 재생성과 .NET 재배포를 함께 수행하세요. 자세한 파일 역할과 명령은 [게임 README](../diamond-game/README.md)에 있습니다.

## 대결 코드와 접속 상태

AI 대결은 혼자 시작할 수 있습니다. 2인 대결은 한 사람이 대결을 만들고 상대방이 8자리 코드를 입력해 참가합니다. 참가자 구분에는 계정 대신 `diamond_session` 쿠키를 사용합니다. 같은 브라우저의 일반 탭끼리는 쿠키를 공유하므로 두 사람의 역할을 시험하려면 서로 다른 브라우저나 별도 브라우저 프로필을 사용하세요.

대결은 생성 시점부터 24시간 동안 보관됩니다. 쿠키를 지우거나 다른 브라우저로 바꾸면 기존 참가자 권한이 이어지지 않습니다. 같은 화면에서 다른 사이트 탭으로 이동했다가 돌아오는 동안에는 경기 상태를 유지하지만, 사이트 전체를 새로 고치면 iframe 안의 대결 주소가 초기화될 수 있습니다. 필요한 대결 코드는 미리 복사해 두세요.

게임 POST 요청에는 사이트의 `/api/session`에서 받은 CSRF 토큰이 필요합니다. 다른 출처의 요청과 참가하지 않은 사용자의 경기 조작은 허용하지 않습니다. 요청 본문은 최대 4KB입니다. 게임 조회의 속도 제한은 IP당 분당 900회로, 기록실의 일일 조회·행 수 한도와 분리합니다. 새 대결 생성에는 IP당 분당 16개, 서버 전체 분당 180개의 추가 제한이 있습니다.

## 빌드와 배포

일반적인 .NET 배포에는 npm이 필요하지 않습니다. 저장소에 포함된 `frontend/diamond/` 번들과 게임 자료를 웹 프로젝트가 복사합니다. `WEB/Dockerfile`도 같은 `dotnet publish` 경로를 사용합니다.

게임의 TypeScript·CSS·선수 자료를 수정했다면 `WEB/diamond-game`에서 다음을 실행한 뒤 변경된 소스와 `WEB/frontend/diamond/` 번들을 함께 커밋하세요.

```sh
npm ci
npm run typecheck
npm test
npm run build
```

이어서 `WEB`에서 웹 서버를 게시합니다.

```sh
dotnet publish server/src/NaverSabermetrics.Web/NaverSabermetrics.Web.csproj -c Release -o artifacts/diamond-web
```

새 배포에는 루트 `app.js`, `games.js`, `diamond.js`, `index.html`과 게임 하위 파일을 함께 포함해야 합니다. .NET 빌드는 누락된 게임 HTML을 오류로 처리합니다. 서버용 JSON 자료는 `diamond-data/`에 배치하며 정적 파일 경로에 노출하지 않습니다.

## 격리된 개발 환경에서 미리보기

Windows 사용자 프로필의 암호화 키 저장소를 사용할 수 없는 격리된 개발 환경에서는 `Site:UseEphemeralDevelopmentKeys=true`를 명시적으로 설정할 수 있습니다. `Development` 환경에서만 적용하며 기본적으로 꺼져 있습니다. 메모리 안에서 임시 Data Protection 키를 사용하므로 서버를 재시작한 뒤에는 페이지를 새로 고쳐 CSRF 토큰을 다시 받아야 합니다.

환경 변수로 지정할 때는 `ASPNETCORE_ENVIRONMENT=Development`와 `Site__UseEphemeralDevelopmentKeys=true`를 함께 사용합니다. 이 옵션은 로컬 UI/API 미리보기를 위한 것이며 `Production` 환경에서는 설정값이 `true`여도 적용되지 않습니다. 운영 환경의 키 보관 및 CSRF 검증 방식은 그대로 유지합니다.

## 통합 검증 결과

2026-09-12 통합 검증에서 **42,443개 검사**를 통과했습니다. 기존 TypeScript 엔진과 C# 엔진의 투구·판정 수치 비교, AI 양쪽 역할, PvP 참가 권한과 동시 참가, 반복 명령, 6타석 결과, 대결 상태 저장·복원, 만료와 생성 제한, HTTP CSRF·쿠키·다른 출처 거부·본문 크기 제한을 포함합니다.

검증은 별도 임시 게임 DB와 로컬 HTTP 서버를 사용하며 기록실 원본 DB를 열거나 수정하지 않습니다. 재실행 방법은 [게임 API 검증 안내](../validation/DiamondGame/README.md)에 있습니다. 이 수치는 자동 검사 결과이며 실제 배포 주소의 화면과 조작 확인은 아래 절차로 별도로 진행합니다.

Release 게시 산출물에서도 HTTP 확인 31개를 통과했습니다. 게임 정적 파일, 기존 622경기 카탈로그 조회, 실제 CSRF를 거친 AI·친구 대결, 비공개 자료 경로의 404 응답을 확인했습니다. 로컬 브라우저에서는 타자·투수 시점, 투구 입력과 타격 판정, 기록실 탭 전환 후 경기 복귀, 390px 화면에서 가로 넘침이 없는 배치를 확인했습니다.

## 배포 후 확인

1. `/#diamond` 직접 접근과 홈 → 야구게임 → 경기기록 → 야구게임 전환을 확인합니다.
2. 선수 선택, AI 대결 시작, 타격·투구, 결과 표시를 확인합니다.
3. 서로 다른 브라우저 두 개로 대결 생성·코드 참가·역할별 조작을 확인합니다.
4. 다른 탭으로 이동했을 때 반복 요청이 멈추고 복귀하면 경기가 갱신되는지 확인합니다.
5. 기록실 조회가 계속 동작하고 게임 상태가 별도 `diamond_game.db`에 저장되는지 확인합니다.

게임 프레임이 차단되면 `/diamond/` 응답의 `X-Frame-Options: SAMEORIGIN` 및 CSP의 `frame-ancestors 'self'`를 확인하세요. 다른 페이지에는 기존 iframe 차단 정책을 유지합니다. 게임 HTML·JavaScript가 404이면 소스 재빌드 후 `frontend/diamond/` 번들이 포함된 커밋을 배포했는지 확인하세요. CSRF 오류가 나면 페이지를 새로 고쳐 세션 토큰을 다시 받습니다.
