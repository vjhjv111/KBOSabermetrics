# 기록실 DB와 클라우드 이전 제안

작성·공식 문서 확인: 2026-09-12. 아래는 이전 설계이며 PostgreSQL 전환이나 클라우드 리소스 생성은 실행하지 않았다. 8개 분석실 기능은 기존 SQLite를 읽는다.

## 추천

서버형 DB로 전환한다면 **PostgreSQL**을 권한다. 웹 기록실·분석·게임을 작은 운영 인원으로 공개한다면 **GCP Cloud Run + Cloud SQL for PostgreSQL, 서울 리전**을 우선 검토한다. 이미 AWS를 쓰거나 상시 실행하는 게임 서버와 네트워크를 직접 세밀하게 관리하려면 **ECS Fargate + RDS for PostgreSQL, 서울 리전**도 적합하다. GCP를 추천하는 이유는 이 프로젝트의 컨테이너 배포와 운영 단순화를 위한 판단이지, 모든 조건에서 더 저렴하거나 더 빠르다는 뜻은 아니다.

SQLite도 DBMS다. 이번 선택은 파일을 사용하는 내장형 DBMS에서 서버형 관계형 DBMS로 바꾸는 것이다. 다수의 읽기 조회는 SQLite로도 처리할 수 있다. 전환 효과가 커지는 시점은 웹 서버 여러 대, 수집·정정과 웹 조회의 동시 실행, 게임 상태의 동시 쓰기, 관리형 백업·복구가 필요할 때다. 파일 크기만으로 이전을 결정하지 않는다. [SQLite 공식 사용 지침](https://www.sqlite.org/whentouse.html)

## 현재 코드에서 옮겨야 하는 범위

| 현재 구성 | 전환 대상 | 주의점 |
|---|---|---|
| 기록 DB의 Games, PlateAppearances, Pitches, RunnerEvents, PlayerChanges | PostgreSQL `baseball` 스키마 | 경기·선수·타석·투구 식별자를 그대로 보존 |
| 공식 박스스코어·중계·정정·진단 | `provenance` 또는 동일 관계형 스키마 | 정정된 최종값과 보존된 원문을 분리 |
| 리그 상수·집계 캐시 | `analytics` 요약 테이블 | 수식 버전·데이터 버전·필터를 캐시 키에 포함 |
| `diamond_game.db`의 Matches/CreateLimits | `game` 스키마 | 현재의 Version 조건부 UPDATE와 중복 요청 방지를 보존 |
| `web_state.db`의 사용량 집계 | 공유 PostgreSQL 테이블 | 프로세스별 lock만으로 여러 서버의 한도를 제어하지 않음 |
| 위조 요청 방지 토큰의 Data Protection 키와 quota HMAC 키 | 서버 간 공유되고 지속되는 키 저장소 | 기존 CSRF 토큰 검증과 IP별 사용량 해시를 유지 |
| 원본 통합 JSON/ZIP | GCS 또는 S3 객체 저장소 | 원문은 압축·해시·수집일·소스 URL로 추적, 웹 루트에 배치하지 않음 |

`DatabaseCacheService`, 각 WebService, `DiamondRosterService`, `DiamondGameService`, `QuotaStore`가 SQLite 연결을 직접 만든다. 연결 문자열만 바꾸면 끝나지 않는다. `PRAGMA`, `sqlite_master`, SQLite 날짜 함수, SQL 별칭 대소문자, 정수형 boolean, JSON 및 upsert 처리를 검사해야 한다. `QueryGate`는 현재 Microsoft.Data.Sqlite의 동기 실행을 감싸는 구조이므로 PostgreSQL의 비동기 호출에 맞게 교체한다.

현재 `diamond_session`은 64자리 난수 쿠키이고 서버는 그 SHA256 해시를 게임 참가자 ID로 사용한다. 이 게임 쿠키는 Data Protection 키를 사용하지 않는다. 공유 Data Protection 키는 CSRF 토큰 검증에 필요하며, `quota.key`는 `web_state.db`의 IP별 일일 사용량 해시를 유지한다. 기본 코드에는 운영용 Data Protection 키 저장 위치를 공유 저장소로 지정하는 설정이 없으므로 이전 때 명시적으로 구성한다.

## PostgreSQL을 권하는 이유

현재 핵심은 경기·선수·타석·투구를 결합하는 관계형 분석이다. PostgreSQL의 윈도 함수와 집계, 트랜잭션을 유지하면서 복잡한 정정 메타데이터에는 JSONB를 선택적으로 사용할 수 있다. .NET에서는 Npgsql을 사용하고 공유 `NpgsqlDataSource`를 통해 연결 풀을 관리한다. [Npgsql 공식 연결 지침](https://www.npgsql.org/doc/basic-usage.html)

새 분석마다 전체 투구를 다시 읽지 않도록 시즌/선수/경기/구종별 요약을 만든다. 물리화 뷰는 계산 결과를 저장하고 갱신하는 수단이다. 누락·정정 경기만 재집계하는 별도 요약 테이블도 가능하다. PostgreSQL로 옮긴다는 이유만으로 모든 쿼리가 빨라지는 것은 아니며 실행 계획과 실제 지연을 비교한다. [PostgreSQL 물리화 뷰](https://www.postgresql.org/docs/current/sql-creatematerializedview.html)

MySQL도 구현 가능하지만 기존 관계형 분석을 바꾸면서 얻을 추가 이점이 뚜렷하지 않다. MongoDB/Firestore를 주 DB로 쓰면 현재 JOIN·집계를 상당 부분 다시 설계해야 한다. BigQuery 같은 분석 웨어하우스는 나중에 대규모 탐색·학습용으로 추가할 수 있으나, 실시간 게임 상태의 기본 저장소로 선택하지 않는다.

## 안전하게 옮기는 순서

1. **기준 스냅샷 확정.** 실행 중인 파일을 단순 복사하지 않고 SQLite 백업 기능으로 일관된 스냅샷을 만든다. 시즌별 경기/PA/투구 수, NULL 분포, 원문 해시, 공식 대조 상태, 팀별 합계를 저장한다. 현재 미리보기의 2026년 622경기 사본과 사용자의 전체 DB를 혼동하지 않는다.
2. **조회 계약 분리.** 기존 URL과 반환 JSON을 유지한 채 기록·분석·로스터·게임 저장소 인터페이스를 도입한다. SQLite 구현을 남겨 양쪽을 비교할 수 있도록 한다.
3. **타입과 SQL 명시 변환.** 선수 코드와 경기 코드는 text, 아웃 수·투구 수는 integer/bigint, 날짜는 date, 시각은 timestamptz, 좌표·계산값은 double precision, flag는 boolean으로 정한다. 이닝은 소수 대신 정수 아웃 수를 기준으로 유지한다. 빈 문자열/NULL, UTC/한국 경기일, 소수 정밀도, 잘못된 JSON을 변환 보고서에 남긴다. 대소문자 테이블명을 보존하거나 snake_case로 일괄 바꾸되 암묵적으로 섞지 않는다.
4. **스테이징에 대량 적재.** PostgreSQL COPY/Npgsql binary import로 부모 테이블부터 적재한다. 중복 ID·FK·정정 이력을 검증하고 인덱스와 ANALYZE를 수행한다. 원본 JSON을 처음부터 재파싱하는 작업과 DB 엔진 이전을 한 번에 섞지 않는다. [Npgsql COPY](https://www.npgsql.org/doc/copy.html)
5. **동일 질의 비교.** 시즌/팀/선수별 PA·AB·H·HR·BB·SO·R·RBI·아웃·ER, 기존 WAR와 상수, 8개 분석의 분모·누락·결과를 비교한다. 정수는 정확히 일치, 실수는 지표별 허용 오차를 사전에 정한다. 경기 전·중단 경기·더블헤더·이적·동명이인·정정·부분 좌표·중도 투수 교체를 포함한다.
6. **증분 수집 전환.** 수집 워커가 원문을 객체 저장소에 저장하고 경기 ID+소스 버전/해시로 멱등 적재한다. 한 경기 갱신은 한 트랜잭션으로 처리하고 영향받은 시즌 요약을 갱신한다. 실시간 수집을 두 DB에 무조건 이중 쓰기하기보다는 재실행 가능한 가져오기 작업과 변경 로그를 사용한다.
7. **게임·운영 상태 이전.** `UPDATE ... WHERE Version=이전값`을 유지해 동시 스윙/투구가 서로 덮어쓰지 않게 한다. 사용량 제한, 대결 생성 제한, 키 공유, 만료 정리를 함께 구현한 후 두 웹 인스턴스 사이의 교차 요청을 테스트한다.
8. **전환과 복구 리허설.** 수집을 짧게 멈추고 마지막 증분을 반영한 뒤 웹 연결을 바꾼다. 복구 지점·점검 항목·되돌리는 절차를 기록한다. 전환 뒤 생긴 신규 게임/정정 데이터를 잃지 않도록 롤백 시 재반영 방법도 마련한다.

## AWS와 GCP 구성 비교

| 항목 | GCP 후보 | AWS 후보 |
|---|---|---|
| 웹/.NET 컨테이너 | Cloud Run | ECS Fargate + 로드밸런서 |
| 관계형 DB | Cloud SQL PostgreSQL | RDS PostgreSQL |
| 한국 사용자 중심 리전 | 서울 `asia-northeast3` | 서울 `ap-northeast-2` |
| 원본 JSON/백업 파일 | Cloud Storage | S3 |
| 수집·재집계 | Cloud Run Jobs + Scheduler | ECS 작업 + EventBridge Scheduler |
| 비밀 값·키 | Secret Manager 및 공유 키 저장 구현 | Secrets Manager 및 공유 키 저장 구현 |
| 로그·지표 | Cloud Logging/Monitoring | CloudWatch |
| 선택 이유 | 작은 운영 인원, 요청량 변동, 컨테이너 운영 단순화 | AWS 경험/기존 자원, 상시 워크로드와 네트워크 제어 |

Cloud Run의 로컬 파일시스템은 지속 저장소가 아니다. **현재 SQLite 파일 3개를 그대로 컨테이너에 넣고 여러 인스턴스로 실행하는 것은 이전 완료가 아니다.** 공유 DB와 키 저장을 먼저 해결해야 한다. Cloud Run의 동시 요청 수와 최대 인스턴스 수를 제한하고 `최대 인스턴스 × 인스턴스별 풀 상한 + 수집/관리 연결`이 DB 연결 예산 안에 있도록 설정한다. [컨테이너 계약](https://docs.cloud.google.com/run/docs/container-contract), [Cloud SQL 연결](https://docs.cloud.google.com/sql/docs/postgres/connect-run)

일일 사용량 `Quotas`와 게임 생성 제한 `CreateLimits`는 DB에 저장되지만, API의 분당 요청 제한과 `QueryGate`의 동시 분석 제한은 현재 프로세스별 메모리 상태다. 인스턴스를 늘릴 때 이 제한도 공유 방식으로 바꾸거나 인스턴스별 한도를 전체 예산 안에서 배분해야 한다. 일일 사용량 테이블을 공유 DB로 옮기는 것만으로 분당·동시 요청 제한까지 공유되지는 않는다.

게임이 지속적으로 폴링하는 동안 웹 요청은 계속 발생한다. 기록실만 가끔 여는 서비스와 같은 비용으로 계산하지 않는다. 초기에는 단일 웹 인스턴스로 배포 검증하고, 공유 상태의 동시성 검증을 통과한 뒤 확장한다. 게임 시작 지연이 중요하면 최소 인스턴스 1을 검토하되 유휴 비용도 견적에 넣는다. [Cloud Run 과금](https://cloud.google.com/run/pricing)

## 비용과 초기 크기

아직 동시 이용 규모·월 예산·전체 DB의 실제 크기가 확정되지 않아 월액을 견적으로 확정하지 않는다. **계산기에서 같은 서울 리전, 730시간, 동일 CPU/RAM/SSD, 동일 HA 조건으로 비교**해야 한다. 가격 페이지의 기본 미국 리전 숫자를 서울 가격으로 인용하지 않는다.

검증용 출발점은 DB 전용 2 vCPU·4~8 GiB RAM, 저장공간은 실제 적재 크기+인덱스+증가분+운영 여유를 측정해서 정한다. 이는 성능을 보장하는 운영 사양이 아니다. 전체 시즌 분석의 캐시 미스, 게임 폴링/스윙, 수집 병행 부하에서 p50/p95·CPU·I/O·메모리·연결 대기를 측정한 뒤 결정한다. 게임 요청을 무거운 분석과 분리한 연결 풀/동시성 예산도 검토한다.

월 총액은 DB 컴퓨트 + SSD/IO + 백업/PITR + 웹 컴퓨트/요청 + 수집 작업 + 네트워크/로드밸런서 + 객체 저장 + 로그 + 세금으로 계산한다. Cloud SQL이나 RDS의 상시 DB 비용은 웹 요청이 없는 시간에도 따로 고려해야 한다. 고가용성 설정을 켠 견적과 끈 견적을 섞어 비교하지 않는다. RDS T계열을 후보로 삼으면 지속적인 분석 부하의 CPU 크레딧 추가 요금도 확인한다. [RDS PostgreSQL 가격](https://aws.amazon.com/rds/postgresql/pricing/), [Cloud SQL 가격](https://cloud.google.com/sql/pricing), [Fargate 가격](https://aws.amazon.com/fargate/pricing/)

비용 상한은 예산 알림만으로 보장되지 않는다. 웹 최대 인스턴스, 쿼리 제한, 결과 캐시, 로그 보존기간과 원문 보관정책을 함께 정한다. 자동 백업뿐 아니라 별도 테스트 DB로 복구하는 절차까지 확인하고 운영을 시작한다.

## 이 작업에서 실행한 것과 남은 것

- 실행: SQLite 기반 분석실 8개 기능 및 조회/화면 검증, 현 코드의 이전 범위 조사.
- 설계만 작성: PostgreSQL 스키마/저장소 구현, 데이터 적재, 공유 게임·운영 상태 전환, AWS/GCP 배포.
- 실제 이전 시작 전 필요한 값: 월 예산, 예상 동시 이용자/게임 수, 전체 DB 크기와 일별 증가량, 허용 중단시간 및 백업 보존기간.
