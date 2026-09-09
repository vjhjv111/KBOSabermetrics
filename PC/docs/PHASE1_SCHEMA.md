# Phase 1 정규화 스키마

## 1. 처리 흐름

```text
NaverRelayResponse
  → 원본 DTO 역직렬화
  → textRelays 시간순 정렬
  → 릴레이 그룹 분류
  → 이벤트 전후 상태 정규화
  → 타석·투구·주루·교체·관리 이벤트 생성
  → 경기 최종 라인과 교차 검증
  → NormalizedGame
```

원본 JSON의 `textRelays`는 화면 표시용 묶음입니다. 하나의 묶음이 반드시 하나의 공식 타석이라는 가정은 하지 않습니다.

## 2. ID 설계

원본 `seqno`는 선수 교체 이벤트가 다음 릴레이에 반복되면서 중복될 수 있습니다. 따라서 다음 ID를 별도로 생성합니다.

```text
RelayGroupId      gameId:relay:sourceRelayNo:idxChronologicalIndex
EventId           gameId:event:sourceRelayNo:idxRelayIndex:optionIndex
PlateAppearanceId gameId:pa:sourceRelayNo:idxRelayIndex
```

원본 식별값은 삭제하지 않고 다음 필드에 보존합니다.

- `SourceRelayNo`
- `SourceSeqNo`
- `SourceOptionIndex`
- `IsDuplicateSourceSeqNo`

## 3. 릴레이 그룹

`RelayGroup.GroupType`은 다음 값 중 하나입니다.

| 값 | 판정 기준 |
|---|---|
| `InningMarker` | 이벤트가 전부 `type=0` |
| `CompletedPlateAppearance` | `type=13/23` 타자 결과 포함 |
| `InterruptedPlateAppearance` | 타석 시작·투구·주자 결과가 있고 3아웃으로 종료 |
| `PrePlateSubstitution` | 타석 시작과 선수 교체만 있고 실제 투구가 없음 |
| `PlayerChangeOnly` | 이벤트가 전부 `type=2` |
| `AdministrativeOnly` | 이벤트가 전부 `type=7` |
| `GameSummary` | 이벤트가 전부 `type=99` |
| `IncompletePlateAppearance` | 시작 후 투구 또는 관리 이벤트는 있으나 타자 결과 없음 |
| `Unknown` | 현재 규칙에 해당하지 않음 |

`Unknown` 그룹도 원본 이벤트를 그대로 보존하며 `UNKNOWN_RELAY_GROUP` 진단을 추가합니다.
`StateBefore`와 `StateAfter`는 그룹의 첫 이벤트 전 상태와 마지막 이벤트 후 상태입니다.

## 4. 이벤트 타입

| 원본 type | 정규화 타입 | 의미 |
|---:|---|---|
| 0 | `InningMarker` | 이닝 시작·초말 전환 |
| 1 | `Pitch` | 실제 투구 |
| 2 | `PlayerChange` | 선수 교체·수비 위치 변경 |
| 7 | `Administrative` | 피치클락·마운드 방문·판독·퇴장 |
| 8 | `PlateAppearanceStart` | 타석 시작 |
| 13, 23 | `BatterResult` | 타자의 타석 결과 |
| 14, 24 | `RunnerResult` | 주자 진루·득점·아웃 |
| 99 | `GameSummary` | 경기 종료 요약 |
| 기타 | `Unknown` | 원본을 보존하고 진단 생성 |

## 5. 경기 상태

`CurrentGameState` 원본은 문자열 중심의 Raw DTO로 유지하고, 파서에서 `GameStateSnapshot`으로 변환합니다.

주요 필드는 다음과 같습니다.

- 홈·원정 득점, 안타, 볼넷, 실책
- 투수·타자 pcode 및 이름
- 볼·스트라이크·아웃
- 각 루의 타순 슬롯
- 각 루 슬롯을 현재 출전 선수로 해석한 주자 pcode와 이름

### 공격 팀 플래그

```text
homeOrAway = "0" → Away batting → 초
homeOrAway = "1" → Home batting → 말
```

### 베이스 값

`base1`, `base2`, `base3`은 선수가 차지한 현재 타순 슬롯입니다. 예를 들어 `base1="8"`이면 해당 공격 팀의 현재 8번 타순 선수를 1루 주자로 해석합니다.

선수 교체 이벤트를 시간순으로 적용해 다음 상태를 유지합니다.

```text
TeamSide + BatOrder → 현재 출전 PlayerIdentity
```

## 6. 타석

`PlateAppearance`는 공식 완료 타석뿐 아니라 주자 아웃으로 중단된 타석도 보존합니다.

주요 필드:

- `Status`
- `IsOfficialPlateAppearance`
- `BatterPcode`, `BatterName`, `BatOrder`
- `PitcherPcode`, `PitcherName`
- `FinalPitcherPcode`, `FinalPitcherName`
- `ResultRawType`, `ResultText`
- `Outcome`
- `StateBefore`, `StateAfter`
- `RunsScored`, `OutsRecorded`
- `ActualPitchCount`, `MaximumDisplayPitchNumber`
- WPA 및 승리확률
- 하위 투구·주자·교체·관리 이벤트 ID

### 타자 결과 선택

타석 결과는 릴레이의 마지막 이벤트가 아니라 처음 발견되는 `type=13/23` 이벤트에서 선택합니다. 뒤에 주자 이벤트나 비디오 판독이 붙어도 타자의 결과 문장은 바뀌지 않습니다.

### 투수 선택

1. 실제 투구가 있으면 첫 `PitchEvent`의 투수
2. 실제 투구가 없으면 타자 결과 이벤트의 투수
3. 종료 시 투수는 `FinalPitcherPcode`에 별도로 보존

## 7. 타격 결과

`BattingOutcome`에는 다음 정보가 들어갑니다.

- 단타·2루타·3루타·홈런
- 볼넷·고의4구·몸에 맞는 볼
- 삼진·땅볼·플라이·라인드라이브·번트
- 희생플라이·희생번트·병살타
- 실책 출루·야수선택
- 타구 유형
- 타구 방향과 주 수비 위치
- 홈런 거리
- 타수 포함 여부, 안타 여부, 출루 여부, 총루타
- `WasRecognized`

알 수 없는 결과는 삭제하지 않고 원문을 유지하며 진단을 생성합니다.

## 8. 투구

`PitchEvent`는 `type=1`만 생성합니다. 피치클락 자동 볼·스트라이크는 `AdministrativeEvent`로 분리합니다.

주요 필드:

- 실제 투구 순서 `ActualPitchIndex`
- 공식 표시 순서 `DisplayPitchNumber`
- 투수·타자
- 투구 전후 볼카운트
- 원본 및 정규화 투구 결과
- 구종·구속
- PTS 연결 여부
- 초기 위치·속도·가속도
- 홈플레이트 통과 좌우·높이
- 타자별 스트라이크존 상·하단
- 존 포함 여부
- 스윙·헛스윙·컨택·인플레이·루킹 파생 플래그

### pitchResult

| 코드 | 의미 |
|---|---|
| B | 볼 |
| F | 파울 |
| H | 인플레이 |
| S | 헛스윙 |
| T | 루킹 스트라이크 |
| W | 번트 파울 |

미확인 코드는 `Unknown`으로 두고 `RawPitchResult`에 보존합니다.

### PTS 높이 계산

`crossPlateY`는 세로 높이가 아니라 앞뒤 기준면 좌표입니다. 다음 방정식의 0 이상인 가장 작은 해 `t`를 구합니다.

```text
y0 + vy0·t + 0.5·ay·t² = crossPlateY
```

같은 시간의 좌우와 높이를 계산합니다.

```text
x(t) = x0 + vx0·t + 0.5·ax·t²
z(t) = z0 + vz0·t + 0.5·az·t²
```

결과는 `CalculatedCrossPlateX`, `CalculatedCrossPlateZ`, `TimeToPlateSeconds`에 저장합니다.

## 9. 주자 이벤트

`RunnerEvent`는 다음을 보존합니다.

- 주자 pcode·이름
- 출발 루와 도착 루
- 득점·아웃 여부
- 포스아웃·태그아웃·도루실패·견제사 등 이벤트 종류
- 타구, 도루, 폭투, 실책, 주루방해 등 진루 이유
- 이벤트 전후 상태
- 파싱 성공 여부

## 10. 선수 교체

구조화된 `playerChange`가 있으면 텍스트보다 우선 사용합니다.

- 들어온 선수·나간 선수
- 기존·신규 포지션
- 수비 위치 이동 선수
- 타순
- 투수 교체·대타·대주자 플래그
- 교체 팀

파싱된 교체만 활성 타순 상태에 적용하며, 미분류 교체는 원문과 진단만 보존합니다.

## 11. 관리 이벤트

현재 분류 대상:

- 코칭스태프 마운드 방문
- 포수 마운드 방문
- 투수판 이탈
- 피치클락 위반
- 비디오 판독
- 퇴장

피치클락 이벤트에는 자동 볼·스트라이크 증가량을 기록합니다. 비디오 판독은 원판정, 최종판정, 번복 여부를 보존합니다.

## 12. 검증과 진단

`GameConsistencyValidator`는 다음을 확인합니다.

- 원본과 정규화된 그룹·이벤트·투구·주루·교체·관리 이벤트 건수
- 완료 타석 수
- 모든 ID의 유일성
- 이벤트와 상위 개체의 참조 무결성
- 초·말 제목과 `homeOrAway`의 일치
- 경기 최종 타격 라인의 `PA/AB/H/HR/BB/HBP/SO` 합계와 완료 타석 결과

`ParserSummary`에는 미분류 그룹·이벤트·결과, PTS 계산 실패, 최종 타격 라인 불일치 건수도 함께 기록합니다.
오류나 미분류 데이터는 `Diagnostics`에 누적합니다. 데이터는 가능한 한 보존하고, 조용히 버리지 않습니다.
