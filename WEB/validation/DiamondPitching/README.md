# Pitch distribution and collision validation

This executable references the production game engine. It reads the baseball warehouse in read-only mode and writes only statistical reports to a supplied scratch directory.

The last argument selects `baseline`, `observed`, or `contracts`:

```powershell
dotnet run -c Release --project WEB/validation/DiamondPitching/DiamondPitching.Validation.csproj -- "C:/path/to/KBOSabermetrics/WEB/diamond-game/lib" "C:/path/to/readonly/season.db" "C:/path/to/scratch" 10000 observed

dotnet run -c Release --project WEB/validation/DiamondPitching/DiamondPitching.Validation.csproj -- "C:/path/to/KBOSabermetrics/WEB/diamond-game/lib" "C:/path/to/readonly/season.db" "C:/path/to/scratch" 10000 contracts
```

The baseline explicitly reproduces the original AI-ready placement policy. It selects players with at least 100 PA/TBF, balances six batter/pitcher hand and underhand combinations, and simulates no-swing pitches. This measures game behavior; it is not a prediction of official HBP totals. The output includes zone rate, HBP rate, collision body part, each handedness group, and individual pitch observations. A fixed seeded random sequence makes comparisons reproducible.

The final 2026 run after zone-region-preserving bin coarsening used the same player selection and seed:

| Policy | Pitches | HBP | HBP rate | Zone rate | Body contacts inside zone |
|---|---:|---:|---:|---:|---:|
| Original AI-ready placement | 10,000 | 882 | 8.82% | 45.07% | 141 |
| Observed joint pitch profile | 10,000 | 57 | 0.57% | 44.22% | 0 |

The original policy produced 1,023 body contacts in total; most HBP contacts were torso (489) or arms (278). The real 2026 warehouse contained 737 HBP among 191,466 pitches (0.3849%). The balanced test roster has different pitcher weights from the full league, and the simulation omits swings, so the observed simulation rate should not be read as an exact reproduction of that league total.

The synthetic contract suite passes **15,149 checks**. It covers:

- Feet/strike-zone normalization, no coordinate mirroring, and wide endpoints without clamping.
- Pitch type, location, and velocity sampled together by recorded counts, batting side, and ball/strike count.
- No extra random scatter on a safe measured endpoint.
- Only the last pitch of an HBP plate appearance marked as HBP, including a plate appearance spanning two pitchers.
- Missing measurements, sparse profiles, official HBP per total pitch, and cancellation.
- Private profile persistence, exclusion of bins from public views, cache isolation, DB revision refresh, and continuing an existing match after restart without the source DB.
- Over 5,000 distinct locations, nonfinite inputs, and invalid zones; bounded coarsening preserves sample mass, HBP classification, and all nine strike-zone regions.

A controlled profile with 0.38% HBP produced 30 physically verified HBP in 10,000 pitches, with zero ordinary zone/body collisions. The separate existing DiamondGame suite also passes all 42,443 original engine parity, AI/PvP, persistence, and HTTP checks. Raw pitch reports and synthetic databases remain in the supplied scratch directory, outside this project.
