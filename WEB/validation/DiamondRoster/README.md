# DB roster regression checks

This executable references the real web project and creates a small synthetic baseball database and separate match state database under a supplied scratch directory. It never writes to an existing user database. The tests exercise season/identity isolation, baseball aggregation, measured pitch and discipline data, input validation, and persisted match snapshots.

Run from the repository root, supplying absolute paths:

```powershell
dotnet run -c Release --project WEB/validation/DiamondRoster/DiamondRoster.Validation.csproj -- "C:/path/to/KBOSabermetrics/WEB/diamond-game/lib" "C:/path/to/scratch"
```

Fixtures include two regular seasons, a traded player, a distinct player with the same display name, a postseason game, all-star teams, a pitcher without final box-score statistics, and absent pitch/profile measurements. Expected rates are derived independently from the fixture counts.

The completed suite has 96 checks. It also starts temporary loopback HTTP servers for the real roster endpoint, checks that no-store responses use camelCase fields, and verifies 400/503 errors. Source database hashes are compared around roster reads.

To measure an existing database without changing it, use the optional read-only probe. It reports the selected season/date, roster counts, missing hands, fallback arsenals, and cold/warm load times:

```powershell
dotnet run -c Release --project WEB/validation/DiamondRoster/DiamondRoster.Validation.csproj -- --probe "C:/path/to/readonly/season.db"
```
