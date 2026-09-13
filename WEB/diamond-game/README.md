# DIAMOND 야구게임

실제 시즌 기록으로 구성한 구단을 맡아 타격과 투구를 조작하는 웹 야구 게임입니다. 사이트의 **야구게임** 탭(`/#diamond`) 또는 `/diamond/index.html`에서 실행합니다. React·Three.js 화면과 ASP.NET Core/.NET 8 판정 서버를 사용하며, 경기 상태는 별도 SQLite에 저장합니다.

이 폴더는 기존 기록실 웹사이트에 통합하는 전체 경기 소스입니다. 별도로 내보낸 Sites/Vinext/Cloudflare D1 기반 6타석 게임 ZIP과 실행 환경이 다릅니다.

## 플레이 범위

| 모드 | 현재 구현 |
| --- | --- |
| 리그 | 10개 구단, 기본 팀당 144경기·전체 720경기. 72경기/18경기 단축 시즌, 일정·순위·개인 기록, 다음 시즌 진행 |
| 친선 AI | 선택한 두 구단으로 한 경기. 직접 타격·투구 또는 공수/경기 자동 진행 |
| 친선 PvP | 8자리 코드로 두 사용자가 각자 구단을 맡음. 3아웃마다 타격·투구 역할 교대 |
| 마이 플레이어 | 이름·포지션·좌우/양타·좌우투·투구폼·외형 생성, 리그 출전 기록·XP·레벨·훈련 |
| 타석 연습 | 기존 AI/PvP 6타석 합계 점수 대결. 전체 경기와 별도 저장 |

전체 경기는 9이닝, 동점이면 최대 12회이며 12회 동점은 무승부입니다. 홈팀 우세 시 9회 말 생략과 끝내기, 주자·볼넷·사구·삼진·병살·희생플라이, 투수·타자 교체를 처리합니다. 일정은 게임용 균형 대진표이며 실제 KBO 일정이나 포스트시즌을 재현하지 않습니다.

직접 조작하는 범위는 **타격 조준·스윙 시각, 투수의 구종·조준·릴리스**입니다. 야수 이동·타구 추적 카메라·주루는 서버 결과를 보여 주는 연출입니다. 야수를 키보드로 이동하거나 도루·번트·송구를 직접 명령하는 기능은 없습니다. AI/리그에는 자동 진행이 있고 PvP에는 없습니다.

PC에서는 마우스·방향키로 조준하고 **야구장 화면을 클릭하면 스윙**합니다. 모바일은 원하는 위치를 짧게 탭하고 떼면 스윙합니다. 투수는 **경기장 안의 구종 버튼**으로 선택한 뒤 코스를 조준하고 같은 화면의 투구 버튼을 누르고 놓습니다. 전체화면에서도 이 버튼들을 사용할 수 있고 SPACE 조작도 유지합니다. 구종·투구 버튼 영역은 3D 화면과 분리해 스트라이크존을 가리지 않습니다. 화면을 세로로 밀면 페이지가 스크롤됩니다. 스윙 입력 95ms 뒤에 배트가 목표점을 통과합니다. 연습·빠르게·실제 구속 속도를 선택하며 실제 구속은 1.00배입니다.

## 저장 코드로 이어하기

상단 **저장·불러오기**에서 내 저장 코드를 확인·복사하거나 다른 기기의 코드를 입력할 수 있습니다. 한 번 발급된 난수 코드는 이후 자동 저장되는 리그·진행 중인 리그 경기·커스텀 선수의 최신 상태를 계속 가리킵니다. 같은 서버에 접속한 다른 브라우저에서도 이어할 수 있으며 서버를 재시작해도 코드가 유지됩니다.

코드를 불러오면 현재 브라우저의 활성 저장이 바뀌고 화면이 다시 열립니다. 기존 다른 저장은 삭제되지 않으므로 그 저장의 코드도 먼저 보관하세요. 코드가 있으면 해당 데이터를 플레이할 수 있으므로 개인적으로 보관하세요. 친선/6타석 방에는 각 방의 초대 코드로 접속하며, 6타석 연습의 별도 세션은 이 복원 대상에 포함되지 않습니다. 다른 서버로 옮길 때는 소스뿐 아니라 `diamond_career.db`의 안전한 백업도 옮겨야 기존 코드가 작동합니다.

## 실행

게임 화면을 수정하려면 Node.js **22.13 이상**, npm, 서버에는 **.NET 8 SDK**가 필요합니다. 이 폴더에서 실행합니다.

```sh
npm ci
npm run typecheck
npm test
npm run build
```

결과는 `../frontend/diamond/`에 생성됩니다. 이 프로젝트에는 별도 `npm run dev` 서버가 없습니다. 상위 `WEB` 폴더에서 웹 서버를 게시하고 실행합니다.

```powershell
dotnet publish server/src/NaverSabermetrics.Web/NaverSabermetrics.Web.csproj -c Release -o artifacts/diamond-web
$env:NAVER_SABERMETRICS_DB = 'C:/KBO/sabermetrics_v2.db'
$env:Site__StateDirectory = Join-Path (Get-Location) 'work/diamond-state'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet artifacts/diamond-web/NaverSabermetrics.Web.dll --urls http://127.0.0.1:5080
```

`C:/KBO/sabermetrics_v2.db`는 예시이므로 실제 기록실 SQLite 파일의 절대 경로로 바꾸세요. `http://127.0.0.1:5080/#diamond`를 엽니다. `Development` 실행은 로컬 확인용입니다. 원본의 복사본을 준비하는 기존 절차는 `WEB/scripts/start-local.ps1 -DatabasePath '원본 DB 전체 경로'`에도 있습니다.

Windows에서 npm shim이 `npm-cli.js`를 찾지 못하면 설치된 Node 경로의 npm을 직접 실행할 수 있습니다. 다음은 Node가 기본 위치에 설치된 경우입니다.

```powershell
node 'C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js' ci
node 'C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js' run typecheck
node 'C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js' test
node 'C:/Program Files/nodejs/node_modules/npm/bin/npm-cli.js' run build
```

게임 소스 변경 후 생성 번들과 서버를 함께 다시 빌드하세요. 운영은 ASP.NET Core 프로세스 하나로 실행하며 Node 서버나 D1은 사용하지 않습니다.

## 선수 DB와 저장

`/api/diamond/roster?season=2026`은 설정한 원본 DB의 종료된 정규시즌 경기에서 타자·투수·투타·구속·구종·선구안 기록을 읽습니다. 시즌 생략 시 최신 수집 시즌을 사용합니다. 이적 기록은 선수 ID로 합산하며 동명이인과 시즌을 구분합니다. 원본 DB를 수정하거나 경기 중 외부 사이트를 스크래핑하지 않습니다.

실제 대용량 `sabermetrics_v2.db`는 게임 소스에 포함되지 않습니다. 새 리그/친선 경기 생성에는 원본 DB가 필요하고, 10개 구단 각각 타자 9명과 투수 1명 이상이 있어야 합니다. 시작할 때 선수 기록을 게임 저장에 복사하므로 진행 중인 경기는 원본 DB 변경이나 일시 중단 후에도 같은 기록으로 이어집니다. `lib/*.json`의 기존 2025 스냅샷은 이전 타석 대결 복원·회귀 검사용이며 새 시즌 로스터의 대체 자료가 아닙니다.

`Site:StateDirectory` 아래의 `diamond_game.db`는 기존 타석 연습, `diamond_career.db`는 리그·친선·생성 선수·훈련을 별도 테이블에 저장합니다. 친선은 전용 테이블만 쓰며 리그 순위와 커리어 보상에 반영되지 않습니다. 저장 파일, WAL/SHM, 설정·인증 파일을 공개 정적 폴더에 넣지 마세요. 상세 경로와 API는 [운영 문서](../docs/DIAMOND-GAME.md)에 있습니다.

## 선수와 구장 표현

공용 3D 선수와 실제 3D 구장을 사용합니다. 좌우타·양타 전환과 좌우투 × 오버핸드/사이드암/언더핸드의 **6가지 투구 조합**을 처리합니다. 투구폼은 서로 다른 팔 각도·몸통 이동·스트라이드·팔로스루를 사용하고, 모델 손과 서버 릴리스 좌표를 맞춥니다. 커스텀 신장은 모델·투구 릴리스·사구 충돌에 함께 반영합니다. 체형의 사구 영역은 메시 폭/깊이를 감싸는 원형 캡슐 근사입니다.

생성 선수는 키·체형·피부·머리·장비 색·등번호를 설정하고 미리보기에서 회전과 동작을 확인할 수 있습니다. 수비 모델은 지정 포지션을 먼저 배치하고 나머지 자리를 타순으로 채우며 DH는 제외합니다. 실제 선수의 미수집 수비 포지션과 얼굴을 임의로 실측 정보로 표시하지 않습니다. 친선은 방 생성/참가 시 양쪽 생성 선수의 외형을 함께 저장해 재접속 때도 같은 모습을 표시합니다.

리그에서 완료한 경기의 출전 기록으로 XP와 훈련 포인트를 받습니다. ‘내 선수 차례까지’로 해당 타석/투구 차례까지 자동 진행할 수 있습니다. 훈련은 능력치를 2씩, 최대 95까지 올리며 다음 경기부터 반영합니다. 외형 변경도 진행 중인 경기의 저장된 모습을 덮어쓰지 않습니다. 게임 능력치·물리·자동 진행 결과는 실제 성적을 참고한 변환이며 실제 승률의 정확한 재현을 보장하지 않습니다.

내야 흙 텍스처는 Poly Haven의 Rob Tuytel 제작 **Brown Mud Dry**, CC0 자료를 변환해 포함했습니다. 잔디·하늘·관중·구장 형상·선수 표면은 코드로 생성합니다. 배포 시 [구장 자료 출처 및 라이선스](public/assets/stadium/LICENSES.md)를 함께 보관하세요.

훈련 항목은 타자/야수의 컨택·파워·선구안·주루·수비 5개, 투수의 구속·제구·체력 3개로 구분하며 다른 역할의 훈련은 서버에서도 제한합니다.

## 핵심 코드와 검증

| 파일 | 역할 |
| --- | --- |
| `app/season-page.tsx`, `app/season-match.tsx` | 리그 화면과 경기 조작 |
| `app/exhibition-page.tsx`, `app/career-page.tsx` | 친선 대결·선수 생성/육성 |
| `app/page.tsx` | 기존 6타석 연습 |
| `app/action-scene.tsx`, `lib/stadium-world.ts` | 구장·선수·주자·공·카메라 연출 |
| `lib/player-motion.ts`, `lib/player-pose.ts` | 6폼 투구·양손 스윙·관절 연결 |
| `lib/player-model.ts`, `lib/player-appearance.ts` | 공용 선수·장비·커스텀 외형 |
| `lib/season-client.ts`, `lib/match-client.ts` | 순차 입력·동기화·재연결·조건부 조회 |
| `../server/src/NaverSabermetrics.Web/Diamond*.cs` | 권한·판정·경기 규칙·SQLite·커리어 API |

`npm test`는 시계, 조작, 판정, 로스터, 동작, 수비 배치, 키/체형 반영을 검사합니다. 선택 검사 `node tests/pitch-deliveries.mjs --server`는 설치된 .NET 8 SDK로 실제 C# 릴리스·체형 헬퍼와 TypeScript를 비교합니다. 관련 실행 명령과 현재 확인한 결과는 [운영 문서의 검증](../docs/DIAMOND-GAME.md#검증)에 있습니다. 자동 검사 통과는 운영 주소 배포나 모든 기기에서의 화면 검증을 의미하지 않습니다.
