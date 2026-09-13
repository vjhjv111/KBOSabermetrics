# Full baseball game / league / custom career

Authoritative source: `main` at d47c073f2f884d114aebf94ef1cb593ea28ef6dd from vjhjv111/KBOSabermetrics.
Working branch: `codex/full-game-career`. The previous Sites project is a different export and is not the source for this upgrade.

## Required end state

- A three-dimensional stadium with realistic field scale, materials, lighting, uniforms and human proportions. Verify the actual rendered result on desktop and mobile.
- Left/right overhand, sidearm and underhand deliveries, with continuous joints and a ball released from the throwing hand.
- Play full games with batting and pitching input, nine-player orders, base runners, three outs, nine innings, walkoffs, extra innings, substitutions and fatigue. Preserve hit trajectory, sound, location markers and HBP.
- Ten-team leagues: full schedule, AI opponents, standings, player statistics, season completion and subsequent seasons.
- Create a custom player with visible appearance, handedness/delivery, position, attributes; play in the league and earn persistent experience and train attributes that affect play.
- SQLite persistence separate from the source statistics DB; reload/restart recovery, owner isolation, idempotent actions and rewards.
- Preserve existing online friend play while evaluating full-game PvP integration. Do not misrepresent six-PA practice as a full game.

## Completed

- Full-game rules, ten-team seasons, standings, player totals and subsequent seasons are implemented and validated.
- Stadium/player materials, character customization and preview, six deliveries, fielders/runners and camera views are integrated and checked in the browser.
- Career creation, role-specific training, participation rewards, persistent appearance/stat snapshots and next-game progression are implemented and validated.
- Full-game AI and friend PvP, the league/game/career UI, SQLite ownership and recovery, and existing six-PA practice compatibility are validated.
- Source ZIP transfer was checked by extracting into a separate directory and successfully publishing from its `WEB` directory with .NET SDK 8.0.423.

## Completion evidence

Completed build/typecheck, rule and persistence regressions, read-only real-record import, full-game AI/PvP, a complete real-record league season, custom training and restart recovery, browser screenshots and console checks, legacy engine compatibility, and ZIP transfer evidence are recorded in [DIAMOND-CAREER-VALIDATION.md](DIAMOND-CAREER-VALIDATION.md). Reproducible execution and configuration instructions are in [DIAMOND-GAME.md](DIAMOND-GAME.md). The validation document also states the remaining gameplay and rendering limits; completion does not imply GitHub push or deployment of the existing online site.
