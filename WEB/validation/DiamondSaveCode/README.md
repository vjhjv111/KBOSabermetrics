# Persistent save-code regression

Run from the repository root after restoring the project dependencies:

```powershell
dotnet run --project WEB/validation/DiamondSaveCode/DiamondSaveCode.Validation.csproj -- C:/absolute/project/WEB/diamond-game/lib C:/absolute/scratch
```

Use `--no-restore` after a successful package restore. The harness creates a new `save-code-<guid>` directory containing its own `diamond_career.db` and `result.json`. It starts an ephemeral loopback HTTP server with the real season, career, friendly-match and save-code endpoints. The test installs the same antiforgery header contract as the application and uses separate browser cookie containers. Restart checks reconstruct all services and the HTTP server against the same isolated test state.

No baseball record database is supplied or opened. The roster service receives an absent path inside the scratch directory. Synthetic saved league/game snapshots contain identity markers, a pending pitch, runners and counts; they are storage fixtures, not replacements for the full baseball-rules or real-record balance suites. One deliberately disposable test career is removed from this isolated database to verify recovery of a code whose data no longer exists. User play state is never accessed.

Final execution: **90 checks passed**, process exit 0, no compiler warnings.

Coverage:

- Empty owner metadata and refusal to issue an empty save; career-only save; stable code reuse; concurrent first issuance; uppercase/lowercase, whitespace and hyphen normalization.
- Separate alias cookie mapped to the original owner, with no canonical identity or raw cookie exposed in the JSON response. Existing valid cookies are not emitted again by save, season, career or match GET responses.
- Changes made after code issuance appear after recovery: latest league state, active pitch/count/role, custom player, training and pending game reward. The code addresses live automatic saves rather than copying the issuance-time snapshot.
- Existing anonymous data stays reachable both with its original cookie and its own recovery code after that browser switches profiles.
- Empty, malformed, unknown and missing-data code failures keep the current cookie and active career unchanged. Failure without an incoming owner cookie does not create one.
- Original and restored browsers share version conflicts, request idempotency and reward deduplication. Two concurrent training requests consume one cost, and replaying one completed game cannot award it again.
- Server/service restart preserves aliases, code, career and league data; the original code restores into another fresh browser afterward.
- Real HTTP CSRF, Origin, Sec-Fetch-Site, JSON shape and body-size failures. Restore rate limits are checked at 20 requests per minute, 100 per hour and 10 failures per 15-minute bucket, including persistence/cooldown and HTTP 429 without cookie replacement.

This suite covers persistent season, its active game and career recovery. Legacy six-PA sessions and temporary friendly rooms retain their separate invitation/session flow.
