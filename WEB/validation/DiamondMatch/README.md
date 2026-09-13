# Full-game friendly match contract

`DiamondSeasonMatchService` reuses the season rules and pitch engine while persisting only `DiamondSeasonMatches`, `DiamondSeasonMatchRequests`, and `DiamondSeasonMatchLimits`. It never writes a league row, advances other fixtures, or calls the career reward hook.

Register `DiamondSeasonMatchService` as a singleton and call `app.MapDiamondMatch()`. The endpoint is `/api/diamond/match`; POST uses the site's normal CSRF middleware and the shared durable `diamond_owner` cookie.

- `create`: `requestId`, `season`, distinct `hostTeam` and `guestTeam`, `mode` (`ai` or `pvp`), `pace`.
- `join`: `requestId`, eight-character `code`. A cookie returning to its existing team retains that seat. Only two owners can participate.
- Other POST commands require `requestId`, `code`, and current `version`: `pitch`, `swing`, `take`, `tick`, `substitute`, `change-pitcher`; `ready`, `sim-half`, and `sim-game` are AI-only. Pitch/swing field names match the season API and `FullMatchCommand` in `season-types.ts`.
- `appearances` maps only the participating custom player IDs to their visual settings. Host creation and guest joining snapshot those settings into the match DB; later career edits do not change a live match. No owner, training, reward, or other career record is included in that map.
- GET accepts `?code=...`. Both response views contain the same authoritative game; `save.team`, `match.team`, and `action.role` are projected for the requesting participant. `save.game.duel` is stripped from JSON. Input roles change with the half inning.
- GET runs expired-pitch resolution before checking ETag. Its tag includes room code, version, and viewer team. An unchanged `If-None-Match` receives 304 with no body. POST also returns ETag. All responses use `Cache-Control: no-store`.
- Rooms expire after 24 hours. Successful commands refresh expiry; active GET renews at most hourly without changing the input version. The first pitch waits for both PvP players and uses the shared 1.9-second windup.
- Transactions serialize state updates. Repeated request IDs with identical JSON return the current view without repeating the mutation; changed JSON rejects with 409. Stale versions reject with 409. Per-minute creation limits are 8 per owner, 20 per IP, and 150 total.

Run the independent validation executable:

```sh
dotnet run --project WEB/validation/DiamondMatch/DiamondMatch.Validation.csproj -- <absolute WEB/diamond-game/lib> <absolute scratch directory>
```

After package restore, `--no-restore` can be used. In an offline workspace use the repository's `WEB/validation/AnalysisContext/nuget.offline.config` with a populated package cache.

The fixture creates a synthetic ten-team warehouse and isolated writable match DB, starts the actual Kestrel endpoints with an injected clock, and drives two independent HttpClient cookie jars through 216 manual pitches and a complete 12-inning game. It verifies identical outcomes and score, role transitions, single-fixture isolation, independent AI completion, concurrent/replayed/stale requests, own-team substitutions, unauthorized third players, no AI control in PvP, ETag 304/200, source-offline retries and service restart, 24-hour renewal/expiry, creation rate limits, malformed/oversized/cross-origin requests, and an unchanged source DB. It also creates a custom home pitcher and away catcher through the real career endpoint, checks both appearance snapshots, edits the guest's career appearance, and verifies the match's original appearance survives reconnection. The latest run passed 1,441 assertions.
