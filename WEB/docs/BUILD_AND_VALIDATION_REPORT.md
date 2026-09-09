# 업로드 WAR v3 기반 웹 소스 / 검증 패키지

## 제작 결과

사용자가 업로드한 `NaverSabermetrics_V2_KboPitcherWarV3.zip`을 기준으로 서버의 계산 의존 프로젝트를 교체했습니다. 프런트, 실행 csproj·sln, 설정, 로컬 시작 스크립트, 통합 검증 CLI/배치 파일을 한 패키지에 포함했습니다.

기존 전체 DB를 업로드하거나 재파싱할 필요 없이, 로컬에서 원본 DB의 새 웹 조회 복사본을 만들도록 구성했습니다. 웹 조회는 복사본 읽기 전용입니다. 최초 복사 및 캐시 준비는 시간이 걸릴 수 있습니다.

## 계산 코드 보존

- 업로드 의존 파일 53개 중 50개 그대로 유지.
- 수정된 기존 파일 3개는 partial 선언, readonly 연결/메모리 캐시, readonly에서 DB 캐시 저장 생략입니다.
- KboPitcherWarMath.cs, DatabaseCacheService.PitcherWarCalibration.cs는 원본과 SHA-256 동일.
- 웹 표시 계산 식별자: `Uploaded-KboPitcherWarV3-web.1`.
- 동일 DB/조건에서 기존 GetSnapshotAsync와 웹 경로를 비교하는 실행 테스트 포함.

## 이 환경에서 실제 수행한 검사

| 검사 | 결과 |
|---|---|
| 프로젝트 XML/참조 경로 | 4개 프로젝트 확인 |
| 원본/변경 파일 SHA-256 | 보존/변경 범위 확인 |
| SQLite 스키마 실행 | 21개 테이블, 58개 사용자 정의 인덱스 |
| INSERT 열/값 형태 | 21건 통과 |
| SQL 템플릿 EXPLAIN 준비 | 63건 통과 |
| 재구성하지 못한 동적 SQL 템플릿 | 25건: 검사 제외, 미검증 |
| 7경기 ZIP의 원본 필드 구조 | 확인; C# 파싱 실행은 아님 |
| C# 구분자 검사 | 통과; 컴파일 검사가 아님 |
| JavaScript 문법 / 정적 DOM ID | 통과 |

## 아직 실행하지 못한 검사

.NET SDK, C# 컴파일러, 브라우저 실행 파일이 없어 아래를 실행하지 못했습니다. SDK 외부 다운로드도 DNS 연결 실패였습니다.

- dotnet restore/build 및 NuGet 네이티브 종속성 복원
- ASP.NET 실제 프로세스/HTTP 통합 테스트
- Windows 배치/PowerShell 스크립트 실행
- 실제 Chrome/Edge 화면·클릭 테스트
- 사용자 8GB DB의 조회 정확도/실측 성능
- Docker 빌드/운영 배포 및 독립적인 보안 감사

따라서 **배포 검증 완료본이 아니라 소스+재현 가능한 검증 절차**입니다. 모든 검사를 PASS로 간주하지 마세요.

## 사용자 PC에서 실행

1. .NET 8 SDK 설치/준비 후 `verify-web.bat` 실행.
2. 마지막 `ALL_EXECUTED_CHECKS_PASS`를 확인. 실패하면 생성된 `verification-logs.zip` 확인.
3. `start-sample.bat` 실행 후 루프백 브라우저에서 UI 확인.
4. `scripts/start-local.ps1 -DatabasePath '실제 원본 DB 전체 경로'`로 새 웹 복사본 생성.
5. `verify-real-db.bat`으로 기존 V3/웹 경로 결과 대조.
6. 브라우저에서 동일 필터 최초/반복 조회 시간과 데스크톱 값 대조.

검증 배치는 샘플 DB/임시 계정만 만들고 기존 DB·기존 프로그램을 건드리지 않습니다. 로그 ZIP에는 DB/계정 설정/키 파일을 제외하지만, 예외의 로컬 경로나 SQL은 들어갈 수 있습니다.

## 공개 전 주의

정상 로그인한 브라우저에 전달한 데이터는 수집될 수 있습니다. 로그인/CSRF/요청·행 한도는 수집 속도와 과도한 자원 사용을 제한하며 완전 차단이 아닙니다. 로컬 HTTP Development 모드를 공개하지 마세요. 운영 HTTPS/프록시/계정/키 보관/분산 속도 제한/성능 검토는 별도로 필요합니다.
