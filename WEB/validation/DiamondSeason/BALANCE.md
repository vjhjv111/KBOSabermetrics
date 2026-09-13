# Full-game AI balance regression

Measured from the user's read-only `sabermetrics_v2.db`, using the selected season's ten teams, each team's nine highest-PA batters and actual pitcher snapshots. Each row simulates 90 complete games (180 team-games). Counts below are game outcomes, not historical score reproductions or display adjustments.

| Model / roster | Seed | Runs / team-game | AVG | HR / team-game | BB / team-game | K / team-game | Source lineup mean AVG |
|---|---:|---:|---:|---:|---:|---:|---:|
| Before full-game defense, 2026 (root's HTTP benchmark) | random | 39.75 | .62097 | 5.978 | 8.800 | 9.850 | .27710 |
| Full-game defense + pitcher-sensitive AI, 2026 | 193742 | 4.600 | .25460 | .822 | 4.856 | 7.372 | .27710 |
| Full-game defense + pitcher-sensitive AI, 2026 | 44291 | 4.733 | .26064 | .722 | 5.178 | 7.072 | .27710 |
| Full-game defense + pitcher-sensitive AI, 2025 | 193742 | 4.539 | .24843 | .678 | 5.117 | 7.817 | .27175 |

The 270-game follow-up produced roughly 4.5–4.7 runs per team-game, meeting the chosen 4–8 run gameplay target. The guard allows 2–10 runs, .190–.370 AVG and fewer than 3 HR per team-game so that a legitimate roster or seed change does not create a brittle test.

## Cause and correction

The six-PA practice engine intentionally rewarded nearly every reasonably accurate contact as a hit and used a 105 m home-run threshold. Reusing that reward model for both AI lineups gave too few outs, excessive PA, and amplified all counting totals.

`DiamondSeasonFairBall.Resolve` is a separate full-game layer. It keeps physical contact and the swing result, then resolves defense using the pinned hitter H/AB, K/PA, HR/AB, SLG and opposing pitcher WHIP, BB/TBF and K/TBF. Low samples regress toward explicit game baselines. A batted ball must clear the modeled fence (122 m center, 100 m near the lines) to be a HR. Other well-hit balls can become fielding outs. Better manual timing/aim strongly increases hit probability, and a user's ball that clears the fence remains a HR. Optional fielding/speed ratings affect defense and advancement.

Full-game AI timing and aim errors also respond to the actual opposing pitcher's strikeout rate. The exact same full-game result layer applies when a human pitcher faces AI, during automatic league play, and in the friendly match service. The original six-PA practice engine's default behavior is unchanged.

Automatic play skips rendered-body geometry but uses the same swing/contact/count and full-game fielding rules. Its HBP default is a documented game prior, not an invented measured statistic. Interactive AI uses a pinned measured pitch distribution when its revision matches the league snapshot, and a labeled fallback otherwise.

## Reproduction

After restore/build, run:

```text
dotnet run --no-restore --project WEB/validation/DiamondSeason/DiamondSeason.Validation.csproj -- --balance <absolute WEB/diamond-game/lib> <absolute scratch> <absolute source-db> 2026 193742
```

Repeat with `2026 44291` and `2025 193742`. The program writes `balance-<guid>/balance.json` in scratch and fails if the guard is violated. The root's HTTP-level equivalent is `WEB/validation/diamond-season-balance.mjs` with `TEST_URL` set to a rebuilt server.

Separate deterministic regression cases verify that stronger real batting records increase AI hits, stronger opponent WHIP decreases them, better manual contact quality is rewarded, actual center-field fence distance is respected, and fielding/stamina/speed have observable gameplay effects.
