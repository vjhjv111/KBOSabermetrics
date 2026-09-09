# 기존 프로토타입에서 Phase 1로 이전

## 파일별 변경

| 기존 파일 | Phase 1 변경 |
|---|---|
| `GameModels.cs` | nullable 적용, 알 수 없는 필드 보존, 메타데이터 안전 역직렬화 |
| `RelayDataModels.cs` | `lastValidMetricOption` 추가, `homeOrAway` 설명 수정, 상태 문자열·라인업 필드 보완 |
| `PlayModels.cs` | `playerChange` 추가, 기록 필드 nullable, PTS·선수 교체 DTO 보완 |
| `RelayDeserializer.cs` | 문자열/숫자/불리언 혼합 스칼라 대응, 오류 위치를 포함한 `TryDeserialize` 추가 |
| `RelayParser.cs` | 릴레이 그룹 분류, 상태 머신, 타석·투구·주루·교체·관리 이벤트 생성으로 재작성 |
| 신규 `Normalized*.cs` | DB 적재용 정규화 모델과 enum |
| 신규 분류기 | 타격 결과, 주자 결과, 선수 교체, 관리 이벤트, PTS 계산 |
| 신규 CLI | JSON·폴더·ZIP 변환 및 7경기 기준 검증 |

## 기존 잘못된 가정과 변경점

### 1. 릴레이 번호를 타석 PK로 사용

기존:

```csharp
PaNo = play.No;
```

변경:

```text
PlateAppearanceId = gameId + relayNo + chronologicalIndex
```

`play.No`는 `SourceRelayNo`로 보존하지만 단독 PK로 사용하지 않습니다.

### 2. `seqno`를 투구 PK로 사용

기존 `seqno`는 반복되는 선수 교체 이벤트에서 중복됩니다. 새 `EventId`와 `PitchEventId`를 PK 후보로 사용하고 `SourceSeqNo`는 원본 추적용으로만 저장합니다.

### 3. 마지막 이벤트를 결과로 사용

기존:

```csharp
var lastEvent = options[^1];
ResultText = lastEvent.Text;
```

변경:

```csharp
var resultPair = eventPairs.FirstOrDefault(
    pair => pair.Raw.Type == 13 || pair.Raw.Type == 23);
```

주자 이벤트와 판독이 타자 결과를 덮어쓰지 않습니다.

### 4. 첫 상태의 투수 사용

기존:

```csharp
PitcherPcode = options[0].CurrentGameState?.Pitcher;
```

변경:

- 첫 실제 투구의 투수 사용
- 무투구 타석은 결과 이벤트의 투수 사용
- 종료 투수는 별도 저장

### 5. `PitchNum.HasValue`를 실제 투구 조건으로 사용

새 파서는 원본 이벤트 `type=1`을 실제 투구 기준으로 사용합니다. 피치클락 이벤트가 표시 투구 번호를 증가시키더라도 `ActualPitchIndex`에는 포함하지 않습니다.

### 6. `homeOrAway` 의미 수정

기존 주석은 반대였습니다.

```text
0 = 원정 공격 = 초
1 = 홈 공격 = 말
```

### 7. `crossPlateY`를 높이로 사용

`crossPlateY`는 앞뒤 기준면입니다. 높이는 운동방정식으로 계산한 `CalculatedCrossPlateZ`를 사용합니다.

## 호출 코드 변경

기존 타석·투구 목록만 필요한 호출은 유지됩니다.

```csharp
var (plateAppearances, pitchEvents) = RelayParser.Flatten(response);
```

다만 새 기능 전체를 사용하려면 다음 호출로 변경합니다.

```csharp
var game = RelayParser.Parse(response);

var plateAppearances = game.PlateAppearances;
var pitchEvents = game.PitchEvents;
var runnerEvents = game.RunnerEvents;
var playerChanges = game.PlayerChanges;
var administrativeEvents = game.AdministrativeEvents;
var diagnostics = game.Diagnostics;
```

## 속성 변경 대응

기존 `PlateAppearance` 및 `PitchEvent`는 데이터 의미가 확장되어 속성명이 일부 바뀌었습니다.

| 기존 | 새 속성 |
|---|---|
| `PaNo` | `SourceRelayNo` 또는 PK용 `PlateAppearanceId` |
| `HomeOrAway` | `RawHomeOrAway`, `BattingSide` |
| `ResultType` | `ResultRawType`, `Outcome.ResultType` |
| `HomeScoreBefore` | `StateBefore.HomeScore` |
| `AwayScoreBefore` | `StateBefore.AwayScore` |
| `HomeScoreAfter` | `StateAfter.HomeScore` |
| `AwayScoreAfter` | `StateAfter.AwayScore` |
| `PitchCount` | `ActualPitchCount` |
| 투구 `Seqno` | `SourceSeqNo`, PK용 `PitchEventId` |
| 투구 `PitchNum` | `DisplayPitchNumber`, 실제 순서 `ActualPitchIndex` |
| 투구 `Stuff` | `PitchType` |
| 투구 문자열 결과 | `RawPitchResult`, enum `PitchResult` |

## 기존 수집기 연결 위치

기존 `RelayCollector`가 JSON을 저장하는 위치에서 다음 중 하나를 수행합니다.

```csharp
var normalized = RelayParser.ParseJson(rawJson);
await repository.UpsertGameAsync(normalized, cancellationToken);
```

권장 방식은 원본 JSON을 먼저 원본 보관소에 저장한 뒤 정규화 작업을 실행하는 것입니다. 정규화 규칙이 변경돼도 원본에서 다시 생성할 수 있습니다.
