# V2 빌드 및 스모크 테스트

## 1. 빌드 환경 확인

Visual Studio Installer에서 다음 워크로드를 설치합니다.

```text
.NET 데스크톱 개발
```

솔루션은 .NET 8 SDK를 사용합니다.

## 2. 자동 검증

프로젝트 루트에서 실행:

```text
verify-v2.bat
```

수행 작업:

```text
1. dotnet --info 출력
2. NuGet 복원
3. Release 전체 빌드
4. 7경기 ZIP 파싱
5. 알려진 기준값 검증
```

마지막에 다음 문구가 나와야 합니다.

```text
Known 7-game sample validation: PASS
V2 verification completed successfully.
```

## 3. Visual Studio 수동 빌드

```text
NaverSabermetrics.V2.sln 열기
→ 솔루션용 NuGet 패키지 복원
→ 빌드 > 솔루션 다시 빌드
```

시작 프로젝트는 `NaverRelay.Gui`입니다.

## 4. 새 DB 확인

V2 DB:

```text
%LOCALAPPDATA%\NaverSabermetrics\Data\sabermetrics_v2.db
```

기존 DB와 분리되어 있습니다.

새로 만들려면 프로그램 종료 후:

```text
reset-v2-db.bat
```

## 5. 7경기 GUI 스모크 테스트

1. GUI 실행
2. `7경기 샘플` 선택
3. `파싱 시작`
4. 작업 로그에서 `7경기 기준 샘플 검증: PASS` 확인
5. 프로그램 종료
6. 다시 실행
7. 원본을 선택하지 않은 상태에서 경기·타자·투수 탭이 DB에서 표시되는지 확인

## 6. DB 전용 조회 확인

최초 적재 뒤 원본 7경기 ZIP을 다른 폴더로 잠시 이동한 상태에서도 다음 기능이 동작해야 합니다.

```text
경기 목록
타자·투수 통계
선수 검색
선수 개인 페이지
타석·투구 로그
```

이 검사는 조회 경로가 원본 파일에 의존하지 않는지 확인합니다.

## 7. 증분 적재 확인

같은 ZIP을 다시 선택했을 때 기존 문서는 `캐시됨`으로 표시되어야 합니다.

새 JSON 1개를 추가하면 그 경기만 적재되어야 합니다.

## 8. API 확인

```text
run-api.bat
```

브라우저 또는 API 클라이언트에서:

```text
http://localhost:5080/api/health
http://localhost:5080/api/catalog
http://localhost:5080/api/players/search?q=이승현
```

GUI와 API가 같은 Windows 사용자 계정에서 기본 경로를 사용하면 동일한 DB를 조회합니다.

## 9. 5년치 적재 후 확인

- 프로그램 재실행 시 원본 재파싱이 발생하지 않는지
- 시작 직후 메모리가 지속적으로 증가하지 않는지
- 탭 변경 시 관계형 SQL 조회만 수행되는지
- 선수 개인 페이지가 pcode 기준으로 열리는지
- `roundCode == kbo_r` 경기만 정규시즌 통계에 포함되는지

## 10. 문제 보고 시 필요한 정보

```text
Visual Studio 전체 오류 목록
작업 로그 마지막 30줄
DB 파일 크기와 WAL 크기
선택한 필터
문제가 발생한 탭
```
