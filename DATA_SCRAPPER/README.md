# 네이버 + KBO 공식 문자중계 통합 수집기

기존 NaverRelayUI를 복사해 확장한 별도 .NET 8 Windows Forms 프로젝트입니다. 원본 프로젝트는 변경하지 않았습니다.

## 실행 방법

배포 ZIP을 전부 풀고 `NaverKboRelayUI.exe`를 실행합니다. .NET 8 Desktop Runtime이 필요합니다(이 컴퓨터에 설치됨).

1. **날짜별 통합 수집**에서 시작일과 종료일을 선택합니다.
2. 저장 폴더를 선택합니다. 기본값은 문서 폴더의 `NaverKboCombined`입니다.
3. **통합 수집 시작**을 누릅니다.

날짜별 네이버 일정 → 네이버 전체 이닝 중계 → KBO 공식 문자중계/박스스코어 → 선수 ID 연결 → 경기별 통합 JSON 저장 순서로 동작합니다. 경기 URL이나 네이버 JSON 폴더를 따로 입력할 필요가 없습니다. 브라우저도 필요 없습니다.

기존 프로그램과 같이 **1군 정규시즌**을 대상으로 합니다. 경기취소와 KBO가 아닌 일정은 건너뜁니다. 올스타전·포스트시즌 등은 정규시즌으로 잘못 수집하지 않도록 제외합니다.

## 저장 내용과 기준

파일명은 기존처럼 네이버 경기 ID를 사용합니다. 예: `20260911WOSS02026.json`.

- `kboOfficial`: KBO 공식 플레이로그와 양 팀 전체 타자·투수 박스스코어, 이닝별 점수, R/H/E/B.
- `naver`: 기존 방식으로 이닝별 수집을 합친 네이버 JSON. 비교용으로 보존합니다.
- `canonicalSource`: `kboOfficial`. 분석에 사용할 공식 기록의 위치를 명시합니다.
- `collectionStatus`: 두 소스 수집 완료 또는 부분 저장 상태.
- `errors`: 수집 과정에서 확인한 오류.

서로 다른 출처의 점수·안타·타석결과를 하나의 수치로 덮어쓰지 않습니다. **공식 기록을 분석하려면 `kboOfficial.logs`와 `kboOfficial.boxScore`를 사용하세요.** 네이버 중계는 전체를 별도로 보관하지만 공식 기록값을 만드는 데 사용하지 않습니다.

`kboOfficial.boxScore.away/home.batting/pitching.rows`는 KBO 원문 기록입니다. 타자·투수 각 표의 `rowIdentities`에 같은 위치의 선수 식별 정보를 덧붙입니다. `rowNumber`는 해당 표에서 1부터 시작하는 행 번호입니다.

```json
{
  "rowNumber": 1,
  "name": "김태훈",
  "naverPcode": "62360",
  "birthDate": "1992-03-02",
  "backNumber": "27",
  "matchStatus": "matched_unique_name",
  "candidatePcodes": ["62360"]
}
```

위는 필드 설명용 예시입니다. 실제 행 번호는 경기 표의 순서를 따릅니다.

선수 ID 연결은 동일 경기·홈/원정 팀·타자/투수·이름 범위에서 후보 ID가 하나일 때만 합니다. 네이버의 통계, 타석결과, 타순, 배열 순서로 ID를 추정하지 않습니다. 같은 역할의 동명이인·여러 ID 후보·모순된 식별정보는 `naverPcode: null`로 남기고 `matchStatus`에 표시합니다. 모든 로그 문장의 등장인물에 ID를 붙이는 기능은 포함하지 않습니다.

ID 필드는 `naverPcode`로 명명해 출처를 보존합니다. 확인한 일부 선수는 KBO의 `playerId`와 같지만 전 선수에 대해 KBO ID와의 동일성을 검증한 것은 아닙니다. `identityMapping`에는 식별정보 출처·파일명·해시·연결/미확정 행 수를 남깁니다. `matched_unique_name`은 제공된 라인업 안에서 유일하게 매칭됐다는 뜻이며, 네이버 식별정보 자체의 무오류를 보증하지 않습니다.

## 실패·재실행

양쪽 수집이 끝난 파일은 건너뜁니다. 한쪽이 실패하면 수집된 쪽을 부분 저장하고, 같은 날짜로 재실행하면 부족한 부분을 재시도합니다. 부분 저장/실패는 완료와 구분해 화면에 표시됩니다. 경기 진행 중 데이터는 최종 결과로 간주하지 않습니다. 원자적 파일 교체를 사용해 취소나 저장 중 실패로 기존 파일이 반쯤 덮이지 않도록 합니다.

네이버 라인업과 KBO 기록을 연결하지 못해도 KBO 기록값을 네이버 값으로 대체하지 않습니다. 부분 데이터에서는 각 소스와 상태를 확인한 뒤 사용하세요.

## 기존 파싱 탭

**네이버 파싱 (기존 기능)** 탭은 호환성을 위해 남겨둔 네이버 기반 SQLite 파서입니다. 통합 JSON의 `naver` 부분 또는 이전 네이버 JSON을 읽습니다. KBO 공식 결과로 타석을 재판정하거나 DB를 교정하지 않습니다. 공식 분석용 파서는 별도 개발이 필요합니다.

## 소스 빌드

Visual Studio에서 `NaverKboRelayUI.csproj`를 열거나 다음 명령을 사용합니다.

```powershell
dotnet build NaverKboRelayUI.csproj -c Release
dotnet run --project NaverKboRelayUI.csproj
```

AngleSharp 1.1.2(MIT), Microsoft.Data.Sqlite 8.0.8 및 종속 패키지를 사용합니다. 패키지 라이선스는 NuGet 메타데이터를 따릅니다. 실제 검증 범위와 실행 환경 제한은 `검증결과.md`에 정리합니다.
