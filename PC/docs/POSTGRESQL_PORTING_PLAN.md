# PostgreSQL 포팅 계획

V2는 SQLite를 계속 사용하지만 PostgreSQL로 옮길 때 UI와 통계 DTO를 다시 만들지 않도록 계층을 분리했습니다.

## 유지하는 계층

```text
NaverRelay.Parser
NaverRelay.Application
NaverRelay.Gui의 화면 DTO 사용 방식
NaverRelay.Api의 HTTP 계약
```

## 교체하는 계층

```text
NaverRelay.Infrastructure.Sqlite
        ↓
NaverRelay.Infrastructure.PostgreSql
```

PostgreSQL 구현은 다음 Application 계약을 구현합니다.

```text
IWarehouseReadService
IAnalyticsQueryService
IPlayerPageService
```

## 권장 이전 순서

1. SQLite 스키마와 동일한 논리 테이블을 PostgreSQL에 생성
2. pcode, GameId, 날짜, roundCode 복합 인덱스 생성
3. SQLite → PostgreSQL 일괄 복사 도구 작성
4. 조회 서비스에 PostgreSQL 구현 추가
5. API에서 설정에 따라 SQLite/PostgreSQL 구현 선택
6. React/Next.js 프런트엔드 연결

## 서버에서 추가할 항목

- 사용자별 읽기 권한과 관리자 import 권한 분리
- Connection Pool
- 장기 쿼리 타임아웃
- Materialized View 또는 일별/시즌별 집계 테이블
- API 페이징, 정렬, 캐시 헤더
- 백업과 마이그레이션 도구

SQLite V2가 관계형 사실 행을 이미 보존하므로 PostgreSQL 포팅 때 원본 5년치 JSON을 다시 파싱하지 않고 DB 간 변환 경로를 만들 수 있습니다.
