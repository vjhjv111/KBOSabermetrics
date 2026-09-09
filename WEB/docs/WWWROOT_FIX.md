# wwwroot 서버 시작 오류 수정 — 2026-09-10

## 확인된 원인
사용자 제공 오류는 `WebApplication.CreateBuilder` 안의 `StaticWebAssetsLoader`에서 발생한
`DirectoryNotFoundException`입니다. 누락된 경로는 **빌드 출력이 아니라 웹 프로젝트 소스의 wwwroot**입니다.

기존 csproj는 `frontend/**/*`를 `Link="wwwroot/..."`로 연결하여 빌드 출력에는 복사했지만,
프로젝트 경로 `server/src/NaverSabermetrics.Web/wwwroot`를 실제로 만들지 않았습니다.
Development 시작 과정에서 SDK의 런타임 매니페스트가 그 경로를 사용하면 호스트 생성 전에 실패합니다.
Program.cs의 출력 wwwroot 존재 검사만으로는 이 경로를 검증할 수 없었습니다.

`--self-test` 등의 CLI 작업은 서버 호스트 생성 전에 끝나므로 SELF_TEST_ALL_PASS와
그 뒤의 웹 서버 시작 실패가 함께 발생할 수 있습니다. DB 손상을 뜻하는 예외가 아닙니다.

## 변경
- 실제 프로젝트 `wwwroot/index.html`, `app.css`, `app.js`를 패키지에 포함.
- 가상 Link 콘텐츠 항목을 제거하고 실제 wwwroot 파일만 빌드/게시 출력으로 복사.
- `frontend/`는 편집 원본 유지. `SyncFrontendAssets`가 정적 자산 검색 전에 공개 파일 3개만 동기화.
  wwwroot 사본을 직접 편집하지 않습니다. DB/비밀번호/설정/서버 소스를 이 폴더에 복사하지 않습니다.
- Program.cs에서 출력의 필수 파일 3개를 서버 생성 전에 점검.
- `check-static-assets.ps1` 추가: 소스·프로젝트·빌드 출력 파일과 SHA-256 비교,
  런타임 매니페스트 ContentRoots 경로 존재 확인.
- verify-web / start-local은 빌드 후 위 검사를 실행.
- 서버 시작 실패 시 stderr 마지막 60줄을 검증 콘솔에 표시.
- HTTP 검사에 /index.html, /app.css, /app.js의 응답·내용 해시·MIME 검사 추가.
- 인증, HTTPS 정책, 속도·열람 제한, WAR 계산, DB 스키마는 변경하지 않음.

## 실행
서버/Visual Studio 실행을 종료하고 새 폴더에 전체 수정본 압축을 풉니다.
`verify-web.bat` 실행 시 `STATIC_ASSETS_CHECK_PASS` 뒤에 기존 샘플·HTTP 검사가 진행됩니다.
모든 실행 단계가 통과한 경우에만 마지막에 `ALL_EXECUTED_CHECKS_PASS`가 출력됩니다.
실제 DB를 삭제하거나 재파싱할 필요 없습니다.

### 구버전 폴더에서 당장 시도할 수 있는 최소 조치
`verify-web.bat`이 있는 폴더에서 PowerShell을 열어 다음을 실행합니다.

```powershell
New-Item -ItemType Directory -Path ".\server\src\NaverSabermetrics.Web\wwwroot" -Force | Out-Null
.\verify-web.bat
```

이 조치는 기존 Link 설정을 그대로 둔 채 매니페스트용 물리 디렉터리만 만드는 임시 해결입니다.
기존 Link 설정을 유지하면서 프런트 파일까지 임의로 중복 등록하지 마세요.
수정본은 실제 파일 동기화와 사전 검사를 포함해 재발 가능성을 줄였습니다.

## 검증 범위
패키지 구조, XML, HTML/CSS/JS 사본의 바이트 일치, 원본 WAR 계산 파일 해시,
기존 오프라인 SQL/소스 검사와 새 정적 파일 패키지 검사를 수행했습니다.
현재 환경에 .NET SDK/PowerShell이 없고 외부 SDK 다운로드 DNS가 실패하여
실제 C# 빌드·Windows 스크립트·HTTP 실행은 여기서 수행하지 않았습니다.
검증 스크립트 추가는 그 실행이 이미 통과했다는 의미가 아닙니다.
사용자의 실제 DB와 8GB 규모 성능 검사 역시 미실행입니다.

## 공식 문서
https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-8.0
WebRootPath, Development 정적 자산 로딩, 프로젝트/출력의 정적 파일 처리 설명을 참고했습니다.
