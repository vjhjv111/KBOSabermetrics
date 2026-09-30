# FanGraphs 승리확률표로 2016~2023 WPA 교체

2016~2023년 종료 경기의 **모든 공식 타석 WPA**를 FanGraphs WPA Inquirer의 승리확률표로 재계산한다. 수집 WPA, 이전 `fanzai-kbo-remainder-v1` 추산값, 누락분 모두 대상이다. 정규시즌 외 종료 경기도 포함한다. 다른 연도는 변경하지 않는다.

## 표와 계산

- 출처: [FanGraphs WPA Inquirer](https://www.fangraphs.com/tools/wpa-inquirer), 공개 `/api/tools/wpa-inquirer/data` 응답의 `weh`.
- 득점환경은 타석 전후, 모든 연도에 **4.5로 고정**한다. 시즌별 KBO 득점분포를 학습하지 않는다.
- 1~9회 × 초·말 × 0~2아웃 × 8개 주자 배치 × 홈 점수차 −10~+10 = **9,072개 상황**. FanGraphs 도구처럼 9회 이후는 9회 표를 사용한다.
- 표의 소수 원자료를 그대로 사용한다. 화면에서 반올림된 백분율을 역산하지 않는다.
- 홈 타자 WPA = `(타석 후 홈 WE − 타석 전 홈 WE) × 100`. 원정 타자는 부호 반전. DB 단위는 %p이고, 표준 소수 WPA는 저장값 ÷ 100이다.
- 3아웃이면 다음 반이닝의 0아웃·주자 없음 상태로 전환한다. 정상 종료를 확인한 승리·패배는 WE 1·0으로 처리한다.
- 표 밖 점수차를 경계값으로 치환하거나 보간하지 않는다. 상태 누락/모순, 정상 종료 확인 실패, 단축 경기, 무승부의 마지막 타석은 미산출(NULL)이다. 무승부를 임의로 0.5승으로 처리하지 않는다.
- 이 표는 FanGraphs 도구의 WE 표다. KBO 전용으로 보정한 모델이나 FanGraphs가 공표한 KBO 선수 WPA라는 의미는 아니다. 독립 도루·폭투 등은 별도 플레이 WPA로 배분하지 않는다.

검증 사례(득점환경 4.5, 모두 9회말 2사):

| 상황 | 타석 전 홈 WE | 끝내기 홈런 WPA (%p) |
|---|---:|---:|
| 1점 뒤짐, 1루 | 9.799999743700028% | +90.20000025629997 |
| 2점 뒤짐, 1·2루 | 9.040000289678574% | +90.95999971032143 |

## 배포와 실행

확보 자료는 `docs/reference-tables/`에 보관한다.

- `팬그래프-승리확률표-4.5.xlsx`: 전체 상황의 홈·원정 WE, LI, 요청 URL·확보 시각과 끝내기 비교 수식.
- `fangraphs-we-4.5-responses.jsonl.gz`: API 응답의 before/after 상황 값과 요청 URL·시각.
- `fangraphs-we-4.5-manifest.json`: 표 버전, SHA-256, 득점환경, 상황 수.
- `fangraphs-api-range-limit.txt`: 점수차 ±11 요청을 거절한 API 응답과 요청 URL.

필요할 때만 `python WEB/tools/collect_fangraphs_we.py 출력폴더`로 재수집한다. 순차 요청으로 동작하며 HTTP 오류 시 중단하고, 저장된 JSONL에서 재개할 수 있다. 재수집한 표는 검증·해시 갱신 없이 운영 번들을 덮어쓰지 않는다.

표는 `NaverRelay.Infrastructure.Sqlite/ReferenceTables/fangraphs-we-4.5.json`에 있으며 어셈블리 리소스로 포함한다. 웹과 PC는 동일한 표를 사용한다. 실행 시 원본 SHA-256과 전체 상황 키·확률 범위를 검증한다. 파일이 불완전하거나 변조되면 DB를 변경하기 전에 실패한다. 운영 서버는 FanGraphs에 요청하지 않는다.

기존과 같이 `EstimatedWpa__Enabled=true`이면 서버 시작 30초 뒤 Render의 `Site:DatabasePath` / `NAVER_SABERMETRICS_DB`에 직접 적용한다. 기본값은 비활성화다. 이전 WPA 작업 때문에 이미 활성화된 서버는 이 버전 배포 후 전체 교체를 시작한다.

`[WPA]` 로그와 `Site:StateDirectory/wpa-backfill-report.json`에서 버전·표 해시·진행 결과를 확인한다. 보고서의 `Filled`는 새 값이 기록된 전체 건수(기존 값 교체 포함), `Replaced`는 그중 기존 값 교체, `Cleared`는 기존 값이 있었지만 새 방식에서 계산 불가능해 NULL로 바뀐 건수다. `Coverage.Estimated`는 새 표 기준 값, `Collected`는 아직 새 기준으로 전환되지 않은 값이다.

## 저장과 복구 이력

- `WpaRevisionHistory`: 타석 ID, 경기 ID, 이전/새 WPA, 이전/새 버전, 전후 WE, 변경 사유·시각. 기존 값이 0인 경우도 보관한다. 재수집으로 경기 행이 삭제돼도 이력은 남는다.
- `WpaModelHistory`: 기존 시즌 모델 JSON을 보관한다.
- `EstimatedWpaValues`: 현재 표로 계산한 타석의 전후 홈 WE와 WPA.
- `EstimatedWpaBackfillGames`: 버전, 원본 갱신 시각, 잔여 건수, 처리 결과. 완료한 경기는 재실행 시 건너뛴다.

값·이력·집계·체크포인트는 경기별 트랜잭션으로 저장한다. 계산 도중 원본이 바뀌면 해당 경기 전체를 롤백하고 재시도 대상으로 남긴다. 실패해도 완료한 경기는 유지한다. 원자료 `RelayGroups`의 WPA/승리확률과 원본 중계 텍스트는 보존한다.

`PlateAppearances`, 타자 경기 WPA 합계, WPA를 사용하는 구원 등판 집계와 캐시를 함께 갱신한다. 경기 상세 화면은 전환 완료 경기에서 새 타석 WPA·WE를 표시한다. 과거 경기 재수입에도 등록된 표를 적용한다.

기존 값 복원이 필요하면 운영 DB 백업 또는 `WpaRevisionHistory`를 사용한다. 이력을 단순히 삭제하면 원래 값이 복구되는 것은 아니다. 복구 시 타석 값과 경기 집계·캐시를 함께 갱신해야 한다.

## 로컬 검증

원본이 아닌 테스트 DB 복사본을 지정한다.

```powershell
dotnet run --project WEB/validation/EstimatedWpa/EstimatedWpa.Validation.csproj -- --check-table
dotnet run --project WEB/validation/EstimatedWpa/EstimatedWpa.Validation.csproj -- --db '테스트.db' --train --backfill --report '보고서.json'
```

`--train`은 이전 CLI와의 호환 이름이며, 현재는 학습 없이 완성된 표를 등록한다. 같은 명령을 다시 실행해 변경 0건인지 확인할 수 있다. `--year 2018`은 시험 적용 연도를 제한한다. `--preview 원본.json`은 원본 JSON과 DB 복원의 WPA가 일치하는지 검사한다.

확보한 표로 테스트 DB 복사본의 5,817경기·456,343공식 타석을 검증했다. 447,678타석이 계산됐으며, 그중 기존 값 교체 441,475건·누락분 보완 6,203건이다. 기존 추산 중 계산 불가능한 7,023건은 이력 보관 후 NULL로 바꿨고 최종 미산출은 8,665건이다. 범위 밖 연도의 264,950개 WPA와 원본 중계 WPA·WE는 변경되지 않았다.

재실행 및 경기 재수입 후 재실행은 모두 변경 0건이었다. 김상수·지성준의 두 끝내기 사례는 DB, 원본 JSON 재계산, 경기 상세 API에서 각각 +90.2000002563%p와 +90.9599997103%p로 일치했다. 세부 결과는 `reference-tables/fangraphs-we-4.5-local-validation.json`에 보관한다. 이는 로컬 복사본 검증 결과이며 운영 DB 처리 완료 보고서는 별도다.
