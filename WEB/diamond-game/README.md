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

현재 선수 자료는 **2025 정규시즌 타자 20명·투수 22명**입니다. 통합 전 게임에 있던 선수 목록과 수치를 유지합니다. 웹사이트에 연결한 `sabermetrics_v2.db`를 매일 갱신해도 게임 선수가 자동으로 2026 시즌으로 바뀌거나 추가되지는 않습니다.

`lib/players.json`은 선수 목록·기본 기록, `arsenals.json`은 구종 자료, `action-data.json`은 게임 판정에 사용하는 세부 자료입니다. `player-profiles.json`과 `batter-colliders.json`은 투타·신체·동작 및 충돌 판정 자료를 제공합니다. 선수 자료를 갱신할 때는 같은 선수 코드로 이 파일들을 함께 확인하고 게임 번들과 서버를 다시 배포해야 합니다.

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
