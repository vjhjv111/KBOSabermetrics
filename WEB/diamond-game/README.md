# DIAMOND 야구게임

KBO Sabermetrics 웹사이트 상단의 **야구게임** 탭(`/#diamond`)에서 실행하는 타자·투수 시점의 타석 대결입니다. AI 대결과 8자리 코드로 참가하는 2인 대결을 제공합니다.

게임 화면은 React·Three.js로 만들고, 경기 판정과 대결 상태 관리는 기존 ASP.NET Core 서버의 C# 코드에서 수행합니다. 운영 시 별도의 Node 서버나 게임 서버를 실행할 필요가 없습니다.

## 실행과 배포

일반적인 웹 배포는 기존대로 `WEB`에서 .NET 프로젝트를 게시하면 됩니다.

```sh
dotnet publish server/src/NaverSabermetrics.Web/NaverSabermetrics.Web.csproj -c Release -o artifacts/diamond-web
```

저장소의 `WEB/frontend/diamond/`에는 미리 빌드된 게임 화면이 포함됩니다. .NET 빌드가 이 파일을 `wwwroot/diamond/`로 복사하고, 게임 판정 자료는 공개 웹 폴더 밖의 `diamond-data/`에 복사합니다. 기존 Docker 배포도 같은 파일을 포함하며 npm 설치를 요구하지 않습니다.

게임 소스를 수정할 때는 Node.js 22.13 이상이 필요합니다. 이 폴더에서 다음 순서로 실행하세요.

```sh
npm ci
npm run typecheck
npm test
npm run build
```

빌드 결과는 `../frontend/diamond/`에 저장됩니다. 소스 변경과 함께 해당 번들도 커밋한 후 .NET 웹 프로젝트를 다시 빌드·배포하세요. `node_modules/`는 커밋하지 않습니다.

## 선수와 기록

선수 목록은 웹사이트와 같은 기록실 DB에서 가져옵니다. `/api/diamond/roster`가 최신 수집 시즌을 기본으로 제공하며, 시즌·팀·이름으로 타자와 투수를 선택합니다. 종료된 정규시즌의 성적과 투타·선구안, 실제 구종별 평균 구속·구사율을 게임 능력치에 반영합니다. DB를 갱신하면 새 대결에서 새 기록을 선택할 수 있으며 게임 화면을 다시 빌드할 필요는 없습니다.

시작한 대결은 선택 당시의 선수 기록을 서버에 보관합니다. 경기 도중 DB가 갱신되어도 능력치는 유지되고, 브라우저는 서버가 보낸 같은 자료로 장면과 조작을 계산합니다. DB 조회 실패 시에는 재시도를 안내하고 새 대결 생성을 보류합니다. 자료가 없거나 표본이 적으면 화면에서 표시하며 필요한 게임 동작에 기본값을 적용합니다.

`lib/roster.ts`가 DB 선수 목록과 진행 중인 대결의 고정 자료를 연결합니다. 기존 `players.json`, `arsenals.json`, `action-data.json`, `player-profiles.json`은 이전 대결 복원과 수치 회귀 검증을 위해 유지합니다. `batter-colliders.json`은 공통 동작·충돌 자료입니다. 새 선수의 성적을 이 정적 파일에 수동으로 추가할 필요는 없습니다.

게임 결과는 조작과 게임용 판정 규칙에 따른 결과이며 KBO 공식 기록을 수정하지 않습니다.

## 코드 위치

| 위치 | 역할 |
| --- | --- |
| `app/page.tsx` | 선수 선택·경기 조작·서버 요청·탭 활성 상태 |
| `app/action-scene.tsx` | 3D 구장·선수·공의 표현 |
| `app/site-theme.css` | 기록실과 공통으로 사용하는 색상·화면 디자인 |
| `lib/` | 선수 자료·화면 동작·시간 및 투구 표시 계산 |
| `build.mjs` | 배포용 JavaScript·CSS·HTML 생성 |
| `../frontend/diamond.js` | 사이트 탭과 게임 iframe 연결 |
| `../server/src/NaverSabermetrics.Web/Diamond*.cs` | 게임 자료 로딩·판정·상태 저장·HTTP API |

저장 위치, 접속 상태, 대결 코드와 배포 확인 방법은 [웹 운영 문서](../docs/DIAMOND-GAME.md)를 참고하세요.
