# 업로드 WAR v3 연결 변경 기록

기준 ZIP: NaverSabermetrics_V2_KboPitcherWarV3.zip (이번 업로드 실물)
웹 표시 계산 버전: Uploaded-KboPitcherWarV3-web.1

## 유지

Parser 및 Application 프로젝트 전체를 그대로 사용합니다. KboPitcherWarMath와 PitcherWarCalibration 계산 파일을 바꾸지 않았고, 기존 BuildBatter/BuildPitcher 통계 함수도 유지합니다. 이전 FullSituationFilters 수학으로 대체하지 않습니다.

## 웹 어댑터

DatabaseAnalyticsService를 partial로 선언하고 한 역할만 읽는 메서드를 새 파일에 추가했습니다. 원본 Build 함수를 그대로 사용하여 다른 역할의 불필요한 SQL 집계를 생략합니다.
DatabaseCacheService에 readonly 모드를 추가했습니다. 읽기 모드의 초기화는 스키마 검사뿐이며 원본에는 DDL·캐시 쓰기를 하지 않습니다. 계산 결과 임시 캐시는 크기 제한 메모리에 저장합니다. 연결은 풀링하지 않으며 VM progress callback으로 오래 걸리는 SQL 취소를 처리합니다. 상수 저장 함수 호출은 readonly일 때 생략합니다.

## Web

실행 프로젝트/솔루션/정적파일 빌드 연결을 보완했습니다. 기존 웹 초안의 ShowLegacyWar/미확인 경고를 제거하고 ShowWar=true로 변경했습니다. WARIP는 6자리, WAR는 1자리로 표시합니다. 서버 결과 캐시 키에 전체 쿼리 JSON을 넣고 역할/뷰를 분리했습니다. 최근 N일은 선택 범위 내 실제 마지막 경기일 기준을 사용합니다.

## 검증 지원

verify-web.bat / C# CLI 검사는 실제 SDK가 있는 PC에서 실행하는 용도입니다. 동일 DB에서 원본 GetSnapshotAsync와 웹 역할별 경로를 비교합니다. 원본 계산의 타당성을 독립적으로 인증하지는 않습니다.

## 포함하지 않은 것

운영용 실측 DB, 미리 등록된 계정/암호, 검증을 완료한 바이너리, Windows GUI 런타임, 전체 선수 개인페이지 웹 대시보드, 자동 크롤링 절대 방지 장치.
