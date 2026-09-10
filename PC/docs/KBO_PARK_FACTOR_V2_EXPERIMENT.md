# KBO Park Factor v2 Experiment

이 기능은 기존 KBO Pitcher WAR v3 공식을 교체하지 않는 A/B 진단 기능이다.

## KBO PF v2 실험식

1. 같은 구장의 최근 최대 5시즌 `SimpleRunParkFactor`를 사용한다.
2. 최근 시즌 가중치: 30%, 25%, 20%, 15%, 10%.
3. 각 시즌 가중치에 해당 시즌 구장 경기 수도 곱한다.
4. 100경기의 중립 prior로 100 방향 회귀한다.

```
Reliability = MultiYearGames / (MultiYearGames + 100)
RegressedPF = 100 + (RollingRawPF - 100) * Reliability
```

5. 실험 안전범위 85~115로 제한한다.
6. 각 시즌의 투구이닝을 가중치로 리그 평균 PF가 100이 되도록 재중앙화한다.
7. 재중앙화 후 극단값이 재발하지 않도록 85~115 제한과 재중앙화를 반복한다.

이 정책값(최근연도 가중치, 100경기 prior, 85~115)은 확정 공식이 아니라 5년치 DB에서 A/B 비교를 위한 실험 설정이다.

## WAR A/B

동일한 시즌/선수/역할/FIP 계산에 대해 파크팩터만 바꿔 다음을 비교한다.

- Legacy PF 가중평균
- KBO PF v2 가중평균
- SP/RP Replacement FIP-
- 보정 전 fWAR
- 목표 WAR 달성률
- WARIP
- 180IP 기준 WARIP 보정량

KBO PF v2는 현재 실제 선수 WAR 화면에는 적용하지 않는다. `WAR A/B` 결과를 검토한 뒤 승격 여부를 결정한다.
