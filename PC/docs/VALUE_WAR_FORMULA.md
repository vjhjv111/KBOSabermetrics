> 투수 계산은 현재 `KBO_PITCHER_WAR_V3.md`를 사용합니다. 아래 Pitching WAR v1은 초기 버전 기록용입니다.

# Value / WAR / Formula 기능

## 추가된 화면

- 타자 Value·WAR
- 투수 Value·WAR
- 공식·계산기

## Site WAR v1

현재 버전은 데이터 검증을 위한 임시 추정치입니다.

```text
타격 Runs = wRAA
주루 Runs = 0.20 × SB - 0.40 × CS
수비 Runs = 0
포지션 보정 = 0
대체선수 Runs = PA × 20 / 600
RAR = 각 Runs 합계
Site WAR v1 = RAR / 10
```

수비 Runs와 포지션 보정은 아직 0으로 처리합니다. 따라서 공식 KBO 또는 Statiz WAR와 동일하지 않습니다.

## Pitching WAR v1

```text
대체선수 RA9 = 리그 RA9 + 1.0
RAR = (대체선수 RA9 - FIP) × IP / 9
Pitching WAR v1 = RAR / 10
```

## Formula 탭

AVG, OBP, SLG, OPS, ISO, BABIP, BB%, K%, wOBA, wRAA, wRC, wRC+, OPS+, FIP, xFIP, FIP-, xFIP-, Swing%, Contact%, CSW%, RE24, WPA, WAR 공식을 확인할 수 있습니다.

wOBA와 FIP는 직접 값을 입력해 계산할 수 있습니다.

## FanGraphs 포지션 보정

```text
포지션 보정 = Σ(포지션별 추정 수비이닝 / 1,458 × 포지션별 Runs)
```

현재 보정값은 C +12.5, SS +7.5, 2B/3B/CF +2.5, LF/RF -7.5, 1B -12.5, DH/PH -17.5입니다.
정확한 수비이닝이 원본에 없으므로 선발 9.0이닝, 교체 4.5이닝으로 추정하며 GUI에 산정 근거를 표시합니다.
